using BuildingBlocks.Entities.Interceptors;
using HealthChecks.UI.Client;
using Marten;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.FeatureManagement;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Setting this to null makes it use the exact C# property names (PascalCase)
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddJwtAuthentication(
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
    audience: "OrderlyMicroservices");

builder.Services.AddAuthorizationServices();

// Feature management — the CatalogRedisCache flag (env: FeatureManagement__CatalogRedisCache)
// gates the cache drift-repair hosted service and lets ops disable the cache without
// a redeploy.
builder.Services.AddFeatureManagement();

// Cache subsystem (Phase 1).
builder.Services.AddOptions<CatalogOptions>()
    .Bind(builder.Configuration.GetSection(CatalogOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ICatalogCache, RedisCatalogCache>();
builder.Services.AddStackExchangeRedisCache(redisCache =>
{
    redisCache.Configuration = builder.Configuration.GetConnectionString("Redis")!;
});

// Menu read-side (Phase 1).
// Scrutor's Decorate<TInterface, TDecorator>() wraps the inner IMenuReader with
// the cache-on-read decorator; the same pattern is used by Basket.
// https://github.com/khellang/Scrutor
builder.Services.AddScoped<IMenuReader, MenuReader>();
builder.Services.Decorate<IMenuReader, CachedMenuReader>();

// Drift-repair hosted service (Phase 1). The tick logic self-gates on the
// CatalogRedisCache feature flag, so the service can be registered
// unconditionally and toggled at runtime.
builder.Services.AddHostedService<CacheDriftRepairService>();

builder.Services.AddCarter();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
builder.Services.AddMarten(opt =>
{
    opt.Connection(builder.Configuration.GetConnectionString("CatalogDB")!);

    // Explicitly configure Marten to only handle document models (audit/logs)
    opt.Schema.For<OrderSnapshot>();
    opt.Schema.For<OrderModificationLog>();
    opt.Schema.For<OrderItemPriceAudit>();
    opt.Schema.For<NotificationLog>();
}).UseLightweightSessions();

var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("CatalogDB")!);
dataSourceBuilder.UseNodaTime();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    options.AddInterceptors(new AuditableEntityInterceptor());
    options.UseNpgsql(dataSource, npgsqlOptions =>
    {
        npgsqlOptions.UseNodaTime();
    });
});

if(builder.Environment.IsDevelopment())
{
    builder.Services.InitializeMartenWith<CatalogInitialData>();
}

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("CatalogDB")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
await dbContext.Database.MigrateAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapCarter();

app.UseExceptionHandler(options => { });

app.UseHealthChecks("/health",
    new HealthCheckOptions {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

app.Run();
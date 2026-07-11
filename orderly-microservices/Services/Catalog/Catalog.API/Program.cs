using BuildingBlocks.Entities.Interceptors;
using Catalog.API.Health;
using Catalog.API.Infrastructure;
using Catalog.API.Infrastructure.Interceptors;
using HealthChecks.UI.Client;
using Marten;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

// ICurrentUser abstraction. Scoped (one HTTP request → one user).
// HttpContextAccessor is added implicitly by AddJwtAuthentication.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

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

// Phase 3: Ingredient Availability Engine reconcile hosted service. Same
// feature-flag-gated pattern as CacheDriftRepairService (Phase 1). The
// default flag value (`CatalogAvailabilityEngineReconcile=false`) means
// the loop is dormant in production until ops flip the flag.
builder.Services.AddHostedService<IngredientAvailabilityReconcileService>();

// Nightly MenuItemAnalytics drift-repair sweep. Re-validates
// today's analytics rows so consumer-side drop-outs surface within 24h.
// Options bound from `MenuItemAnalyticsNightly` config section.
builder.Services.AddOptions<MenuItemAnalyticsNightlyRecomputeServiceOptions>()
    .Bind(builder.Configuration.GetSection(MenuItemAnalyticsNightlyRecomputeServiceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<MenuItemAnalyticsNightlyRecomputeService>();

// Infrastructure (Phase 2): outbox publisher + dispatcher + MassTransit
// consumer discovery. AddInfrastructureServices also calls
// services.AddMessageBroker(...) so the OrderCompletedIntegrationEventHandler
// in Catalog.API/Messaging/EventHandlers is registered as a MassTransit
// consumer at startup. Tests flip `Outbox:Enabled=false` to skip the
// dispatcher hosted service while keeping the publisher and consumer
// registration active (mirrors Ordering.Infrastructure).
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddCarter();

// Internal helper that appends PriceHistory rows from each
// price-mutating handler. Scoped — shares the request's DbContext so the
// audit row commits in the same transaction as the mutation.
builder.Services.AddScoped<IPriceHistoryRecorder, PriceHistoryRecorder>();
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

// Domain-event dispatch interceptor (pre-commit drain). DI-resolved
// so the IMediator constructor injection works. Mirrors Ordering's setup
// (Ordering.Infrastructure/DependencyInjection.cs lines 17-21).
builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

builder.Services.AddDbContext<CatalogDbContext>((sp, options) =>
{
    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
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
    .AddNpgSql(builder.Configuration.GetConnectionString("CatalogDB")!, tags: new[] { "ready" })
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, tags: new[] { "ready" })
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{builder.Configuration["MessageBroker:UserName"]}:{builder.Configuration["MessageBroker:Password"]}@{builder.Configuration["MessageBroker:Host"]?.Replace("amqp://", "")}",
        name: "messagebroker",
        tags: new[] { "ready", "broker" })
    .AddCheck<OutboxDeadLetterProbe>("outbox_dlq", tags: new[] { "ready" });

var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
await dbContext.Database.MigrateAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapCarter();

app.UseExceptionHandler(options => { });

// /live (always green; process up) and /ready (Postgres + Redis +
// RabbitMQ + outbox dead-letter count). Tripping any check trips /ready →
// the load balancer pulls Catalog out of rotation (per
// CATALOG_SERVICE_PLAN.md §7 Phase 2 health-check spec).
app.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.Run();
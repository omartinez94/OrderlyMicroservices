using BuildingBlocks.Messaging.MassTransit;
using HealthChecks.UI.Client;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NodaTime.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddJwtAuthentication(
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
    audience: "OrderlyMicroservices");

builder.Services.AddAuthorizationServices();

// Add services to the container.
builder.Services.AddCarter();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Setting this to null makes it use the exact C# property names (PascalCase)
    options.SerializerOptions.PropertyNamingPolicy = null;
    
    // Configure System.Text.Json to properly understand NodaTime types!
    options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    // Phase 1 of the Basket plan: register the identity guard BEFORE
    // ValidationBehavior so a 403 short-circuits before any validation
    // cost is paid. LoggingBehavior stays last so it wraps everything.
    cfg.AddOpenBehavior(typeof(BasketIdentityGuardBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
var connectionString = builder.Configuration.GetConnectionString("BasketDB")!;
builder.Services.AddMarten(opt =>
{
    opt.Connection(connectionString);
    opt.CreateDatabasesForTenants(c =>
    {
        var maintenanceDbStr = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        // Specify a db to which to connect in case database needs to be created.
        c.MaintenanceDatabase(maintenanceDbStr);
        c.ForTenant()
            .CheckAgainstPgDatabase();
    });

    // Phase 1 of the Basket plan: tag the Basket document with the
    // current restaurant id so Marten's tenant filter narrows every
    // read/write to the active tenant. Combined with the per-tenant DB
    // creation above, this is defense-in-depth — a caller without the
    // matching restaurantId claim cannot reach another tenant's rows.
    opt.Schema.For<Models.Basket>().MultiTenanted();
})
    .ApplyAllDatabaseChangesOnStartup()
    .UseLightweightSessions();

// Phase 1 of the Basket plan: register the tenant resolver and the
// HttpContextAccessor the ClaimsRestaurantProvider reads from. Scoped
// lifetime matches the per-request scope the provider serves.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentRestaurantProvider, ClaimsRestaurantProvider>();

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();
// Phase 1 of the Basket plan: AddProblemDetails() so every 4xx/5xx
// response flows through the same ProblemDetails factory. Closes the
// empty-403 gap (Results.Forbid() returns no body).
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IBasketRepository, BasketRepository>();
/* Applies decorator pattern using Scrutor. Native DI equivalent:
 * builder.Services.AddScoped<BasketRepository>();
 * builder.Services.AddScoped<IBasketRepository>(p => 
 *     new CachedBasketRepository(
 *         p.GetRequiredService<BasketRepository>(), 
 *         p.GetRequiredService<IDistributedCache>()
 *     )
 * );
*/
builder.Services.Decorate<IBasketRepository, CachedBasketRepository>();

builder.Services.AddStackExchangeRedisCache(rediscache =>
{
    rediscache.Configuration = builder.Configuration.GetConnectionString("Redis")!;
});

// Async comunication services
builder.Services.AddMessageBroker(builder.Configuration);

var grpcClientBuilder = builder.Services.AddGrpcClient<Discount.Grpc.DiscountProtoService.DiscountProtoServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcSettings:DiscountUrl"]!);
});

if (builder.Environment.IsDevelopment())
{
    grpcClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
}

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("BasketDB")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

// Broker reachability under entries.messagebroker (tags ["broker",
// "ready"])
var rabbitConnectionString =
    builder.Configuration.GetValue<string>("MessageBroker:ConnectionString")
    ?? builder.Configuration.GetValue<string>("MessageBroker:Host");
if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddRabbitMQ(
            rabbitConnectionString: rabbitConnectionString,
            name: "messagebroker",
            tags: new[] { "broker", "ready" });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthentication();
app.UseAuthorization();

app.MapCarter();

app.UseExceptionHandler(options => { });

app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

app.Run();

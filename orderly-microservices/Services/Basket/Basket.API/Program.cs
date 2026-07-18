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

    // Phase 2 of the Basket plan: the outbox row lives in the same
    // Marten store as the Basket. MultiTenanted() tags each row with
    // the current restaurant id so the dispatcher's claim query cannot
    // publish across tenants. The dispatcher itself is registered as a
    // hosted service below.
    opt.Schema.For<CheckoutBasketOutboxMessage>().MultiTenanted();
})
    .ApplyAllDatabaseChangesOnStartup()
    .UseLightweightSessions();

// Phase 2.3: register a single ConnectionMultiplexer so the
// BasketIdempotencyFilter can use atomic StringSetAsync(key, value,
// expiry, When.NotExists) — IDistributedCache.SetStringAsync is an
// unconditional write and doesn't expose SETNX. Constructed up-front
// so AddStackExchangeRedisCache can share the same instance via its
// ConnectionMultiplexerFactory (the factory is a Func<Task<...>> with
// no DI access, so capturing the multiplexer at registration time is
// the idiomatic pattern).
var redisMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(
    StackExchange.Redis.ConfigurationOptions.Parse(
        builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(redisMultiplexer);

builder.Services.AddStackExchangeRedisCache(rediscache =>
{
    rediscache.ConnectionMultiplexerFactory = () =>
        Task.FromResult<StackExchange.Redis.IConnectionMultiplexer>(redisMultiplexer);
});

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

// Phase 2.3: register a single ConnectionMultiplexer so the
// BasketIdempotencyFilter can use atomic StringSetAsync(key, value,
// expiry, When.NotExists) — IDistributedCache.SetStringAsync is an
// unconditional write and doesn't expose SETNX. The cache layer uses
// the same singleton via RedisCacheOptions.ConnectionMultiplexerFactory
// (already wired by AddStackExchangeRedisCache above).
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
{
    var configurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(
        builder.Configuration.GetConnectionString("Redis")!);
    return StackExchange.Redis.ConnectionMultiplexer.Connect(configurationOptions);
});

// Async comunication services
builder.Services.AddMessageBroker(builder.Configuration);

// Phase 2 of the Basket plan: outbox dispatcher. The relay polls the
// mt_doc_checkoutbasketoutboxmessage Marten table every
// OutboxOptions.ActivePollInterval and forwards staged rows onto the
// MassTransit broker. Same shape as Discount/Ordering's
// OutboxDispatcher<TContext> but Marten-flavored — see
// BASKET_SERVICE_PLAN.md §6 Phase 2 drift item 1.
builder.Services
    .AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<CheckoutBasketOutboxDispatcher>();

// Phase 2.3: Idempotency-Key filter on POST /api/v1/cart/checkout.
// Mirrors Discount.Grpc's IIdempotencyKeyProvider shape but reads
// Basket:Idempotency:SecretHex (separate from Discount's secret —
// sharing the secret would let a Discount-cache-poisoning bug bleed
// into Basket's namespace). The filter itself is registered as a
// transient so a fresh instance is constructed per request; the
// underlying IConnectionMultiplexer is the shared Singleton.
builder.Services
    .AddOptions<BasketIdempotencyOptions>()
    .Bind(builder.Configuration.GetSection(BasketIdempotencyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IBasketIdempotencyKeyProvider, BasketIdempotencyKeyProvider>();
builder.Services.AddScoped<BasketIdempotencyFilter>();

// Phase 2.4: rate limiter on POST /api/v1/cart/checkout (the only
// "spend money" surface). Fixed-window policy keyed on
// (userId, restaurantId) so a single user can't burst-charge across
// different restaurants, and a restaurant-wide scraper can't burst
// across many users in one tenant. Limit: 5 requests per minute per
// (user, restaurant) pair. The 429 response carries
// Retry-After: <seconds> via the OnRejected callback below. Other
// endpoints (GET/PUT/DELETE on the cart) are idempotent reads or
// trivial local writes — they stay unlimited per plan §0.4.8.
// The partition function + OnRejected callback live in
// Basket.API.RateLimiting.CheckoutRateLimiter so they're
// unit-testable without spinning up the full Basket host.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(Basket.API.RateLimiting.CheckoutRateLimiter.PolicyName, Basket.API.RateLimiting.CheckoutRateLimiter.PartitionFunc);
    options.OnRejected = Basket.API.RateLimiting.CheckoutRateLimiter.OnRejectedAsync;
});

// Phase 2.4: hot-reloadable operator-owned base URL for the RFC 7807
// `type` URI in every ProblemDetails response. Bound from
// Basket:Problems:BaseUrl (override via env var Basket__Problems__BaseUrl
// or appsettings.json — no redeploy needed). IOptionsMonitor reads
// fresh on every CurrentValue access; the CheckoutRateLimiter's
// OnRejected callback reads it per request. The BasketIdempotencyFilter
// takes IOptionsMonitor<BasketProblemDetailsOptions> directly via DI.
builder.Services
    .AddOptions<Basket.API.ProblemDetails.BasketProblemDetailsOptions>()
    .Bind(builder.Configuration.GetSection(Basket.API.ProblemDetails.BasketProblemDetailsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var grpcClientBuilder = builder.Services.AddGrpcClient<Discount.Grpc.DiscountProtoService.DiscountProtoServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcSettings:DiscountUrl"]!);
});

// Phase 2.2: wrap the generated DiscountProtoServiceClient behind
// IDiscountLookup so the cart handlers stay unit-testable (the raw
// client returns AsyncUnaryCall<T>, which NSubstitute can't mock
// cleanly). The GrpcDiscountLookup is internal — only the interface
// is part of the public surface.
builder.Services.AddScoped<Basket.API.Discount.IDiscountLookup, Basket.API.Discount.GrpcDiscountLookup>();

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

// Phase 2.4: hand the static CheckoutRateLimiter a reference to the
// hot-reloadable IOptionsMonitor<BasketProblemDetailsOptions> so the
// OnRejected callback emits the operator-owned `type` URI without
// taking an instance dependency (the rate-limiter API requires
// static delegates). Called once at startup; CurrentValue is read
// per request thereafter.
Basket.API.RateLimiting.CheckoutRateLimiter.Configure(
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Basket.API.ProblemDetails.BasketProblemDetailsOptions>>());

// Configure the HTTP request pipeline.
app.UseAuthentication();
app.UseAuthorization();
// Phase 2.4: UseRateLimiter must come AFTER UseAuthentication +
// UseAuthorization because the checkout policy's partition function
// reads the authenticated principal's userId + restaurantId claims.
// When an endpoint carries .RequireRateLimiting("checkout"), the
// middleware evaluates the partition against the current principal
// and either forwards the request or invokes OnRejected.
app.UseRateLimiter();

app.MapCarter();

app.UseExceptionHandler(options => { });

app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

app.Run();

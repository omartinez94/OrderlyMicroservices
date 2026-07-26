using BuildingBlocks.Messaging.MassTransit;
using HealthChecks.UI.Client;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using NodaTime.Serialization.SystemTextJson;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

    // The outbox row lives in the same
    // Marten store as the Basket. MultiTenanted() tags each row with
    // the current restaurant id so the dispatcher's claim query cannot
    // publish across tenants. The dispatcher itself is registered as a
    // hosted service below.
    opt.Schema.For<CheckoutBasketOutboxMessage>().MultiTenanted();

    // Audit log table for the admin endpoints. Per-tenant
    // partitioning + (RestaurantId, OccurredAt) index shape
    // supports the paged audit query on `GET /api/v1/admin/audit`
    // (planned; not part of the Phase 4 admin-carts surface).
    opt.Schema.For<BasketAuditLogEntry>().MultiTenanted();
})
    .ApplyAllDatabaseChangesOnStartup()
    .UseLightweightSessions();

// Register a single ConnectionMultiplexer so the
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

// OpenAPI generation. The `Swashbuckle.AspNetCore` package
// contributes `AddEndpointsApiExplorer` (so the `ICarterModule`
// routes are enumerable) and `AddSwaggerGen` (the schema generator).
// `MapBasketGroup` re-enables `WithOpenApi()` so every endpoint is
// picked up by the generator.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Basket API",
        Version = "v1",
        Description = "Token-bound cart endpoints (GET / PUT / DELETE / POST checkout) behind YARP. " +
                      "Authentication: Bearer JWT carrying `sub` (user id) and `restaurantId` claims. " +
                      "Tenant isolation: every read/write is filtered by the `ICurrentRestaurantProvider` " +
                      "claim resolver; the per-tenant Marten `basketdb` is created on startup. " +
                      "OpenAPI spec is committed under `docs/api/basket-api-v1.json`.",
    });

    // Bearer auth scheme — the JWT bearer token is the only auth on
    // every cart endpoint. The 2.x Microsoft.OpenApi model uses a
    // different security-requirement shape than the older
    // `OpenApiReference` pattern; Swashbuckle's AddSecurityDefinition
    // alone is enough to surface the bearer scheme in the
    // `securitySchemes` block. Endpoints that need to mark the
    // requirement carry the `[Authorize]` attribute that Swashbuckle
    // also picks up.
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the Identity.API-issued JWT (no `Bearer ` prefix).",
    });
});

builder.Services.AddExceptionHandler<CustomExceptionHandler>();
// AddProblemDetails() so every 4xx/5xx
// response flows through the same ProblemDetails factory. Closes the
// empty-403 gap (Results.Forbid() returns no body).
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IBasketRepository, BasketRepository>();
/* Applies decorator pattern using Scrutor. Native DI equivalent:
 * builder.Services.AddScoped<BasketRepository>();
 * builder.Services.AddScoped<IBasketRepository>(p =>
 *     new CachedBasketRepository(
 *         p.GetRequiredService<BasketRepository>(),
 *         p.GetRequiredService<IDistributedCache>(),
 *         p.GetRequiredService<IBasketCacheLockRegistry>()
 *     )
 * );
*/
builder.Services.Decorate<IBasketRepository, CachedBasketRepository>();

// Per-(user, restaurant) single-flight gate registry.
// Singleton lifetime — the SemaphoreSlim entries persist across
// requests so concurrent cache misses coalesce into ONE inner-query
// instead of N. Disposal wires into IHostApplicationLifetime so the
// pending waiters are cancelled at host shutdown (capped by
// IHostOptions.ShutdownTimeout).
//
// Drift item: the previous code re-registered
// AddStackExchangeRedisCache (line 118-121) and IConnectionMultiplexer
// (line 129) a second time. The first registration, above, is the
// only one — IDistributedCache + the atomic StringSetAsync multiplexer
// share a single connection. The duplicate block was reachable but
// dead — DI resolves the LAST registration, so the upstream
// ConnectionMultiplexerFactory wire was silently ignored. Removed.
builder.Services.AddSingleton<IBasketCacheLockRegistry, BasketCacheLockRegistry>();

// Async comunication services
builder.Services.AddMessageBroker(builder.Configuration);

// Outbox dispatcher. The relay polls the
// mt_doc_checkoutbasketoutboxmessage Marten table every
// OutboxOptions.ActivePollInterval and forwards staged rows onto the
// MassTransit broker. Same shape as Discount/Ordering's
// OutboxDispatcher<TContext> but Marten-flavored.
builder.Services
    .AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<CheckoutBasketOutboxDispatcher>();

// Expiry sweep hosted service. Walks the Marten Basket
// collection for carts whose ExpiresAt is in the past and deletes
// them (no event publish — the cart is abandoned, not checked out).
// Default cadence is 5 minutes; configurable via
// Basket:ExpirySweep:Interval / :BatchSize / :Enabled in appsettings.
builder.Services
    .AddOptions<Basket.API.Services.BasketOptions>()
    .Bind(builder.Configuration.GetSection(Basket.API.Services.BasketOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<Basket.API.Services.BasketExpirySweepService>();

// Audit log. Singleton — opens a fresh Marten session
// per write (mirrors the sweep service's scope-per-tick pattern).
builder.Services.AddSingleton<Basket.API.Audit.IBasketAuditLog, Basket.API.Audit.MartenBasketAuditLog>();

// Idempotency-Key filter on POST /api/v1/cart/checkout.
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

// Rate limiter on POST /api/v1/cart/checkout (the only
// "spend money" surface). Fixed-window policy keyed on
// (userId, restaurantId) so a single user can't burst-charge across
// different restaurants, and a restaurant-wide scraper can't burst
// across many users in one tenant. Limit: 5 requests per minute per
// (user, restaurant) pair. The 429 response carries
// Retry-After: <seconds> via the OnRejected callback below. Other
// endpoints (GET/PUT/DELETE on the cart) are idempotent reads or
// trivial local writes — they stay unlimited.
// The partition function + OnRejected callback live in
// Basket.API.RateLimiting.CheckoutRateLimiter so they're
// unit-testable without spinning up the full Basket host.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(Basket.API.RateLimiting.CheckoutRateLimiter.PolicyName, Basket.API.RateLimiting.CheckoutRateLimiter.PartitionFunc);
    options.OnRejected = Basket.API.RateLimiting.CheckoutRateLimiter.OnRejectedAsync;
});

// Hot-reloadable operator-owned base URL for the RFC 7807
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

// OpenTelemetry tracing + metrics. The pipeline emits:
//
//   - ASP.NET Core spans (request lifecycle)
//   - HttpClient spans (the gRPC client to Discount, the
//     IdempotencyFilter's StackExchange.Redis multiplexer, etc.)
//   - Marten spans (Marten's own ActivitySource "Marten")
//   - MassTransit spans (publish/consume lifecycle)
//   - Npgsql spans (raw Postgres queries — important for the
//     CachedBasketRepository + outbox dispatcher)
//
// The OTLP exporter is configured with the bound OtelOptions
// (Endpoint / ServiceName / ServiceVersion). When
// OpenTelemetry:Enabled = false the pipeline still builds (so the
// host can boot in tests) but emits no spans.
builder.Services
    .AddOptions<OtelOptions>()
    .Bind(builder.Configuration.GetSection(OtelOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
var otelOptions = builder.Configuration
    .GetSection(OtelOptions.SectionName)
    .Get<OtelOptions>() ?? new OtelOptions();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: otelOptions.ServiceName, serviceVersion: otelOptions.ServiceVersion))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Marten")
            .AddSource("MassTransit")
            .AddNpgsql();
        if (otelOptions.Enabled)
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.Endpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation();
        if (otelOptions.Enabled)
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.Endpoint));
        }
    });

var grpcClientBuilder = builder.Services.AddGrpcClient<Discount.Grpc.DiscountProtoService.DiscountProtoServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcSettings:DiscountUrl"]!);
});

// Wrap the gRPC client in the standard Polly v8 resilience
// pipeline (Microsoft.Extensions.Http.Resilience, which uses the
// ResiliencePipelineRegistry). Three policies stacked:
//
//   1. Retry — 3 attempts with exponential backoff + jitter
//      (`UseJitter = true` to avoid thundering-herd alignment when
//      the broker recovers).
//   2. Circuit breaker — opens after 5 consecutive failures in
//      a 30s rolling window, breaks for 30s, then half-opens.
//      Without the breaker, a hard outage on Discount would
//      queue retries indefinitely and starve the basket thread
//      pool.
//   3. Attempt timeout — 3s per call. Stacked BEFORE the total
//      request timeout so each retry has its own budget. The
//      total request timeout is 8s (3 attempts × ~3s + jitter).
//
// The basket handlers call `GetDiscountAsync` via `IDiscountLookup`
// (GrpcDiscountLookup), which already fail-closes on a thrown
// exception — the resilience pipeline does not change the
// error-translation path; it just limits blast radius during a
// Discount outage.
grpcClientBuilder.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.UseJitter = true;
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;

    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 5;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
});

// Wrap the generated DiscountProtoServiceClient behind
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
    // Process-alive checks. Used by /live: returns 200 as long as
    // the host is up. No external dependencies — orchestrators
    // (Kubernetes, ECS) use this for liveness probes so a transient
    // Postgres or Redis blip does not kill the pod.
    .AddNpgSql(builder.Configuration.GetConnectionString("BasketDB")!, tags: new[] { "live", "ready" })
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, tags: new[] { "live", "ready" });

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

// /live + /ready split. The two routes mirror the
// Kubernetes liveness / readiness probe distinction. /live only
// checks the host process (no external dependencies) — orchestrators
// use it to decide whether to restart the pod. /ready checks every
// backing store the host needs to serve traffic (Postgres + Redis
// + RabbitMQ + the BasketExpirySweepService + the
// CheckoutBasketOutboxDispatcher).
//
// The previous code mounted a single /health that ran every check
// indiscriminately. Kubernetes would then 503 the liveness probe
// during a transient broker blip and restart the pod — a needless
// recovery cycle. The split is the standard pattern.
//
// `UIResponseWriter.WriteHealthCheckUIResponse` renders the full
// JSON shape on both routes for human debugging.

var app = builder.Build();

// Pre-resolve the cache-lock registry and register an
// ApplicationStopping callback that disposes it. The host's
// IHostOptions.ShutdownTimeout (default 30s) bounds how long
// in-flight callers have to drain — once cancelled, the next
// AcquireAsync raises OperationCanceledException immediately so
// the waiters don't block the shutdown.
{
    var registry = app.Services.GetRequiredService<Basket.API.Caching.IBasketCacheLockRegistry>();
    var lifetime = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        // Fire-and-forget: DisposeAsync returns a ValueTask that
        // semantically completes "fast" (cancel + dispose
        // semaphores), but the registry semaphore dispose path can
        // touch kernel handles — log + swallow if it ever throws
        // rather than crashing the host.
        try
        {
            var valueTask = registry switch
            {
                IAsyncDisposable asyncDisposable => asyncDisposable.DisposeAsync(),
                _ => ValueTask.CompletedTask,
            };
            if (!valueTask.IsCompletedSuccessfully)
            {
                // Detach: the awaiter continues in the background;
                // the registry's stopper has already cancelled the
                // CTS so any pending caller raises quickly. Capture
                // via a side effect — the host shutdown will not
                // wait on this task.
                _ = valueTask.AsTask();
            }
        }
        catch (Exception ex)
        {
            // Defensive — the registry's dispose path is robust to
            // per-semaphore failures (ObjectDisposedException,
            // SemaphoreFullException). This catch is the last line
            // of defence so an anomalous failure doesn't crash the
            // host.
            app.Logger.LogWarning(ex, "BasketCacheLockRegistry dispose failed at ApplicationStopping.");
        }
    });
}

// Hand the static CheckoutRateLimiter a reference to the
// hot-reloadable IOptionsMonitor<BasketProblemDetailsOptions> so the
// OnRejected callback emits the operator-owned `type` URI without
// taking an instance dependency (the rate-limiter API requires
// static delegates). Called once at startup; CurrentValue is read
// per request thereafter.
Basket.API.RateLimiting.CheckoutRateLimiter.Configure(
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Basket.API.ProblemDetails.BasketProblemDetailsOptions>>());

// Configure the HTTP request pipeline.
app.UseMiddleware<CorrelationIdActivityMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Swagger / OpenAPI. The package `Swashbuckle.AspNetCore`
// provides `AddEndpointsApiExplorer` + `AddSwaggerGen` + the
// `UseSwaggerUI` middleware. The MapBasketGroup extension adds
// `WithOpenApi()` so every endpoint is picked up by the generator
// (re-enabled in this phase — Phase 1 deferred WithOpenApi() because
// the package wasn't yet a project dependency).
//
// UseSwaggerUI is gated on `IsDevelopment()` so production
// deployments don't expose the schema surface. The generated
// `swagger.json` is committed under `docs/api/basket-api-v1.json`
// on every phase commit that changes an endpoint — see
// BASKET_SERVICE_PLAN.md §6 Phase 4 doc-update scope.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Basket API v1"));
}
// UseRateLimiter must come AFTER UseAuthentication +
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

// /live + /ready split. The two routes mirror the
// Kubernetes liveness / readiness probe distinction. /live only
// checks the host process (no external dependencies) — orchestrators
// use it to decide whether to restart the pod. /ready checks every
// backing store the host needs to serve traffic (Postgres + Redis
// + RabbitMQ + the BasketExpirySweepService + the
// CheckoutBasketOutboxDispatcher).
app.UseHealthChecks("/live", new HealthCheckOptions
{
    Predicate = _ => false, // no checks — process alive is enough
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.UseHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.Run();

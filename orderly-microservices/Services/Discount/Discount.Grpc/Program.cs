using BuildingBlocks.Dev;
using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Multitenancy;
using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Health;
using Discount.Grpc.Messaging.Outbox;
using Discount.Grpc.Options;
using Discount.Grpc.Services;
using HealthChecks.UI.Client;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// JWT bearer against Identity authority; per-method permission policies evaluated
// by DiscountAuthorizationInterceptor (gRPC's [Authorize(Policy=...)] is silently ignored).
builder.Services.AddJwtAuthenticationWithDevFallback(
    builder.Environment,
    builder.Configuration,
    authority: builder.Configuration["Jwt:Authority"] ?? "https://localhost:5057",
    audience: builder.Configuration["Jwt:Audience"] ?? "OrderlyMicroservices");
builder.Services.AddDiscountPolicies();

// gRPC + dev-only reflection service. MapGrpcReflectionService is registered
// only in Development so production doesn't leak the schema to anyone who
// can reach the port.
//
// The DiscountAuthorizationInterceptor is registered as a server-side
// interceptor: gRPC's [Authorize(Policy=...)] is silently ignored (gRPC
// services aren't routed through the MVC pipeline), so a global
// interceptor is the project's actual enforcement mechanism. The
// per-method permission map it consults is built at startup by
// AddDiscountPolicies() (which reflects over DiscountService,
// DiscountRuleService, and RewardCodeService).
//
// Interceptor pipeline ordering: DiscountAuthorizationInterceptor must
// run AFTER the auth middleware (so HttpContext.User is populated) but
// BEFORE the gRPC method body. ASP.NET Core gRPC executes interceptors
// in registration order, and AddDiscountPolicies() registers the
// authorization services before this AddGrpc call — so the IAuthorizationService
// is available to the interceptor by the time UnaryServerHandler fires.
builder.Services.AddGrpc(o =>
{
    o.Interceptors.Add<DiscountAuthorizationInterceptor>();
});
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddGrpcReflection();
}
builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

// Tenant scoping: IHttpContextAccessor feeds ClaimsRestaurantProvider which
// supplies the global query filter's per-request tenant GUID.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentRestaurantProvider, ClaimsRestaurantProvider>();

// PostgreSQL: build a single NpgsqlDataSource with the NodaTime plugin so
// NodaTime.Instant maps natively to `timestamp with time zone`. The data
// source is consumed by UseNpgsql below; EF reuses it internally via
// DbContextOptions. The pattern mirrors Catalog.API/Program.cs:141-159.
// EnableRetryOnFailure is intentionally NOT added in Phase 1 — the outbox
// dispatcher uses Database.BeginTransactionAsync, which conflicts with
// EF's retrying execution strategy (see plan §10.3). Phase 2 introduces
// ExecutionStrategy wrapping project-wide.
var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("Database")!);
dataSourceBuilder.UseNodaTime();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<DiscountContext>((sp, options) =>
{
    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
        .UseNpgsql(dataSource, npgsqlOptions => npgsqlOptions.UseNodaTime());
});

// Outbox: bind options, configure
// MassTransit with an in-memory bus, register the scoped
// publisher + the dispatcher as a hosted service. The dispatcher injects
// BrokerHealthState so the broker-circuit /ready probe can read the counter.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

// Bind DiscountOptions before AddMassTransit so the conditional
// AddConsumer<> reads the flag values that DiscountOptions would've
// bound via AddOptions + ValidateDataAnnotations + ValidateOnStart.
// (MassTransit's configuration callback fires during service
// registration, before AddOptions validation — so we read the raw
// Configuration section directly here.)
var discountOptionsSection = builder.Configuration.GetSection(DiscountOptions.SectionName);
var enableMenuItem = discountOptionsSection.GetValue<bool>(nameof(DiscountOptions.EnableMenuItemChangedConsumer));
var enableRestaurantConfig = discountOptionsSection.GetValue<bool>(nameof(DiscountOptions.EnableRestaurantConfigChangedConsumer));
// Phase 5 — disabled by default. Notification v1 publishes
// FeedbackSubmittedIntegrationEvent; until that service ships, the consumer
// endpoint does not materialize (no orphaned queue, no silent consumption).
// The flag flips on by configuration change only — no recompile.
var enableFeedbackSubmitted = discountOptionsSection.GetValue<bool>(nameof(DiscountOptions.EnableFeedbackSubmittedConsumer));
// disabled by default. Ordering publishes OrderCreatedIntegrationEvent
// when the Ordering plan's `Phase 8 OrderCreatedConsumer stub` is enabled; this
// consumer wires up automatically when the operator flips the flag. No
// recompile needed (mirrors FeedbackSubmittedConsumer's conditional registration).
var enableOrderCreated = discountOptionsSection.GetValue<bool>(nameof(DiscountOptions.EnableOrderCreatedConsumer));

builder.Services.AddMassTransit(o =>
{
    o.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));

    // Conditional AddConsumer — flipping the config
    // flag to false stops the bus from creating a fresh queue on the
    // next boot, retiring the consumer without a code change. Default
    // values are true; consumers wire up automatically unless an
    // operator explicitly disables them.
    if (enableMenuItem)
    {
        o.AddConsumer<Discount.Grpc.Messaging.EventHandlers.MenuItemChangedConsumer>();
    }

    if (enableRestaurantConfig)
    {
        o.AddConsumer<Discount.Grpc.Messaging.EventHandlers.RestaurantConfigurationChangedConsumer>();
    }

    if (enableFeedbackSubmitted)
    {
        o.AddConsumer<Discount.Grpc.Messaging.EventHandlers.FeedbackSubmittedConsumer>();
    }

    if (enableOrderCreated)
    {
        o.AddConsumer<Discount.Grpc.Messaging.EventHandlers.OrderCreatedConsumer>();
    }
});

builder.Services.AddScoped<IOutboxPublisher, DiscountOutboxPublisher>();
builder.Services.AddSingleton<BrokerHealthState>();
builder.Services.AddHostedService<DiscountOutboxDispatcher>();

// Discount service options: bind + [Range] + ValidateOnStart. The
// options class itself is in Discount.Grpc.Options.DiscountOptions.
builder.Services.AddOptions<DiscountOptions>()
    .Bind(builder.Configuration.GetSection(DiscountOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Idempotency-Key provider (HMAC-SHA256 server-side secret). Wired to a
// middleware; it ships registered as a singleton
// so the wiring commit is additive.
builder.Services.AddSingleton<IIdempotencyKeyProvider, IdempotencyKeyProvider>();

// Readiness probes — /live + /ready split (mirrors Catalog).
builder.Services.AddDiscountHealthChecks(builder.Configuration);

// Expiry sweep — soft-deletes coupons whose ExpirationDate has passed.
builder.Services.Configure<DiscountExpirySweepOptions>(
    builder.Configuration.GetSection(DiscountExpirySweepOptions.SectionName));
builder.Services.AddHostedService<DiscountExpirySweepService>();

var app = builder.Build();

// Configure the HTTP request pipeline.

// Inline-await migration (was Data/Extensions.UseMigration(), which was
// fire-and-forget). Mirrors Catalog.API/Program.cs:181-183. Must happen
// BEFORE MapGrpcService so the schema is in place when gRPC traffic
// arrives. The EF runner applies any pending migrations under
// `Migrations/`. Top-level statements with `await` compile to async Task
// Main, so `app.Run()` after this still works synchronously.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DiscountContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGrpcService<DiscountService>();
app.MapGrpcService<DiscountRuleService>();
app.MapGrpcService<RewardCodeService>();

// gRPC reflection — development only. The package reference is on
// unconditionally; the registration is gated.
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

// Health endpoints. /live is liveness-only (no checks attached);
// /ready is tagged "ready" and uses UIResponseWriter for JSON body
// shape parity with Catalog's /health endpoint.
app.MapHealthChecks("/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();

// Mark the entry point class for WebApplicationFactory<Program> in tests.
public partial class Program;

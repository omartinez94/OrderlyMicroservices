using System.Threading.RateLimiting;
using BuildingBlocks.Dev;
using BuildingBlocks.Observability;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: traces + metrics + logs. The YARP gateway is the
// trace parent for every downstream call; this is the entry point
// of every distributed trace. Wired through the shared
// `BuildingBlocks.Observability.AddOrderlyOpenTelemetry` extension
// so the OTel pipeline shape is consistent across every Orderly
// service.
builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.YarpGateway");
builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);

// =====================================================================
// Phase 6 of the Trust Root Hardening plan (§6.6 + §10.4): the YARP
// gateway now:
//   1. Validates inbound JWTs against the Identity authority (the
//      same dev-fallback scheme the downstream services use, so
//      HS256 dev tokens and RS256 prod tokens both work).
//   2. Registers a CORS policy (default name = "Default") reading
//      its allowed origins from `Cors:AllowedOrigins` config.
//   3. Trusts `ForwardedHeaders` from the docker network range
//      (configurable via `ForwardedHeaders:KnownNetworks`).
//   4. Exposes an anonymous `/health` endpoint for container
//      orchestrators (Docker HEALTHCHECK, K8s liveness/readiness).
// Per-route / per-cluster authorization policies are referenced
// from `appsettings.json` via the `AuthorizationPolicy` metadata
// key — the same metadata is read by YARP's `MapReverseProxy`.
// =====================================================================

// JWT bearer against the Identity authority. The dev fallback
// allows HS256-signed tokens in Development environments and
// RS256-signed tokens in non-Development (per the BuildingBlocks.Dev
// extension's gating).
builder.Services.AddJwtAuthenticationWithDevFallback(
    builder.Environment,
    builder.Configuration,
    authority: builder.Configuration.GetValue<string>("IdentityServiceUrl")
        ?? throw new InvalidOperationException(
            "IdentityServiceUrl config key is required so the gateway can validate inbound JWTs."),
    audience: builder.Configuration.GetValue<string>("Jwt:Audience") ?? "OrderlyMicroservices");

// Empty AddAuthorization — YARP applies the per-route / per-cluster
// policies from the appsettings metadata; no named policies are
// registered here. AddAuthorization() is still required so the
// [Authorize] metadata on a route is honored.
builder.Services.AddAuthorization();

// CORS. The default policy reads its allowed origins from
// `Cors:AllowedOrigins` (an array of strings). Pre-flight OPTIONS
// requests are handled implicitly by ASP.NET Core's UseCors.
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>();

    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// ForwardedHeaders. The KnownNetworks list is environment-specific
// (docker network range in dev, K8s pod range in prod). Read from
// `ForwardedHeaders:KnownNetworks`. Without KnownNetworks, the
// X-Forwarded-For header is ignored by default — which is the
// safe but non-functional posture the audit flagged.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var networks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks")
        .Get<string[]>() ?? Array.Empty<string>();

    foreach (var cidr in networks)
    {
        if (System.Net.IPNetwork.TryParse(cidr, out var network))
        {
            // KnownIPNetworks is the .NET 8+ replacement for the
            // deprecated KnownNetworks — same semantics, different
            // collection type.
            options.KnownIPNetworks.Add(network);
        }
    }

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
});

// Rate limiter. Per-user partition
// when authenticated, per-host fallback otherwise. The plan defers
// tenant-aware rate-limit to a future plan.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("fixed", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// YARP itself. The AuthorizationPolicy + CorsPolicy metadata on
// each route / cluster is what wires the per-endpoint gating.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// =====================================================================
// Middleware pipeline. Order matters:
//   1. UseForwardedHeaders — populate the real client IP / scheme
//      BEFORE auth so audit logs see the upstream IP, not the
//      gateway's loopback.
//   2. UseCors — handle pre-flight OPTIONS before auth so the SPA
//      preflight never has to carry a token.
//   3. MapGet("/health", ...) — anonymous, MUST run before auth
//      so Docker HEALTHCHECK + K8s probes never need a token.
//   4. UseAuthentication + UseAuthorization — populate
//      HttpContext.User; YARP reads it for the per-route
//      AuthorizationPolicy checks.
//   5. UseRateLimiter — after auth so anonymous callers can't
//      consume rate-limit budget they should get a 401 for first.
//   6. MapReverseProxy — the actual proxy.
// =====================================================================

app.UseForwardedHeaders();

app.UseCors();

// /health MUST be mapped BEFORE UseAuthentication so orchestrators
// can probe without a token. Anonymous access is explicit via
// .AllowAnonymous().
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapReverseProxy();

app.Run();

/// <summary>
/// Exposes the top-level-statements entry point as a public type so the
/// test project (if added) can build the host via
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;

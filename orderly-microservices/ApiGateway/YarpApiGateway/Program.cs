using System.Threading.RateLimiting;
using BuildingBlocks.Dev;
using BuildingBlocks.Observability;
using Microsoft.AspNetCore.HttpOverrides;

WebApplicationBuilder? builder = WebApplication.CreateBuilder(args);

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
//   4. Exposes anonymous `/health`, `/live`, and `/ready` endpoints for
//      container orchestrators and backwards compatibility.
// Per-route authorization uses YARP's reserved `Default` policy from
// `appsettings.json`, requiring an authenticated user.
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

// YARP's reserved "Default" authorization policy resolves to ASP.NET
// Core's default policy, which requires an authenticated user. Every
// proxied route references that policy in appsettings.json.
builder.Services.AddAuthorization();

// CORS. The default policy reads its allowed origins from
// `Cors:AllowedOrigins` (an array of strings). Pre-flight OPTIONS
// requests are handled implicitly by ASP.NET Core's UseCors.
builder.Services.AddCors(options =>
{
    string[]? allowedOrigins = builder.Configuration
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
    string[]? networks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks")
        .Get<string[]>() ?? Array.Empty<string>();

    foreach (string? cidr in networks)
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
// when authenticated, per-IP fallback otherwise.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("fixed", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// YARP itself. The AuthorizationPolicy, CorsPolicy, and rate-limit
// metadata on each route wire the per-endpoint gating.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

WebApplication? app = builder.Build();

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

// The compatibility and orchestrator probes are explicitly anonymous.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();
app.MapGet("/live", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }))
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

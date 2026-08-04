using BuildingBlocks.Persistence;
using HealthChecks.UI.Client;
using Identity.API.Data;
using Identity.API.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: traces + metrics + logs. Wired through the shared
// `BuildingBlocks.Observability.AddOrderlyOpenTelemetry` extension so
// the OTel pipeline shape is consistent across every Orderly service.
builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.Identity");
builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// OpenAPI: machine-readable contract served at `/openapi/v1.json`. Built on
// the in-box `Microsoft.AspNetCore.OpenApi` (ships with the .NET 10 SDK).
// Identity's Carter modules own their own route group without
// `.WithTags(...)`; the operation transformer below derives tags from the
// route's first non-prefix segment (e.g. `/api/auth/login` → "Auth",
// `/api/roles` → "Roles"). Same transformation pattern would apply to any
// future service that adds Carter modules without per-endpoint tags.
builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer((operation, context, ct) =>
    {
        if (operation.Tags is { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        // RelativePath strips the scheme + host; shape is "/api/auth/login".
        // Strip the "/api/" prefix (if present) and take the first segment.
        // Title-case + singularise-by-stripping-trailing-s so the tag
        // reads naturally in the Swagger UI (e.g. "Permissions" not
        // "Permissions").
        var relativePath = context.Description.RelativePath ?? string.Empty;
        var trimmed = relativePath.TrimStart('/');
        if (trimmed.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        var firstSegment = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(firstSegment))
        {
            // `permissions/{id}` → "Permissions"; `roles` → "Roles";
            // `audit-log` → "Audit Log". Skips parameters + non-identifier
            // segments via the char.IsLetter check.
            var pascal = string.Concat(firstSegment
                .Replace("-", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

            if (pascal.Length > 0 && pascal.Skip(1).All(c => char.IsLetterOrDigit(c) || c == ' '))
            {
                // Microsoft.OpenApi 2.x: OpenApiOperation.Tags is a set of
                // OpenApiTagReference (a pointer to a tag declared in the
                // document's `tags` array). The SDK auto-declares the tag
                // from the reference, so a single-reference set is enough.
                operation.Tags = new HashSet<OpenApiTagReference>
                {
                    new(pascal),
                };
            }
        }

        return Task.CompletedTask;
    });
});

builder.Services.AddCarter();

builder.Services.AddIdentityDbContext(builder.Configuration);
builder.Services.AddOpenIddictServer(builder.Configuration, builder.Environment);
builder.Services.AddAuthorizationServices();

// Phase 2: register the migration hosted service. Replaces the
// inline MigrateAsync call that previously sat inside DataSeeder.SeedDataAsync
// (DataSeeder.cs:18). The migrator runs at host startup with
// exponential-backoff retry and fails fast after
// MigrationTimeoutSeconds (default 120s) — covering Postgres cold-start
// during rolling restart. The seeder now runs after migrations have
// completed, so seed inserts land against the correct schema.
builder.Services.Configure<MigratorHostedServiceOptions>(
    builder.Configuration.GetSection(MigratorHostedServiceOptions.SectionName));
builder.Services.AddHostedService<IdentityMigratorHostedService>();

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

builder.Services.AddExceptionHandler<CustomExceptionHandler>();

builder.Services.AddScoped<ClaimsTransformer>();
builder.Services.AddScoped<AuditLogger>();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Health checks. The Npgsql check is tagged `"ready"` so it only fires on
// the `/ready` readiness probe. Phase 5 split: `/live` always returns 200
// (process up); `/ready` aggregates every tag=`"ready"` check. Pre-Phase-5
// code mounted a single `/health` that ran every check indiscriminately;
// Kubernetes would then 503 the liveness probe during a transient Postgres
// blip and restart the pod — a needless recovery cycle.
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("IdentityDB")!,
        tags: new[] { "ready" });

var app = builder.Build();

app.MapCarter();

// OpenAPI document endpoint. The in-box generator scans every endpoint
// registered via MapCarter() and emits an OpenAPI 3.0 document at the
// canonical `/openapi/v1.json` path. Tags are derived from the route's
// first non-prefix path segment by the operation transformer registered
// above (e.g. `/api/auth/login` → tag "Auth").
app.MapOpenApi();

app.UseExceptionHandler(options => { });

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// /live + /ready split (Phase 5). /live always green (no checks — process
// alive is enough); /ready aggregates every tag=`"ready"` check via
// UIResponseWriter. Mirrors Catalog / Kitchen / Basket shape.
app.MapHealthChecks("/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

await DataSeeder.SeedDataAsync(app.Services, app.Environment);

app.Run();

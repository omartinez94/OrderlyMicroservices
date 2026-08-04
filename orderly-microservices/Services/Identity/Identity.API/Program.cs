using BuildingBlocks.Persistence;
using Identity.API.Data;
using Identity.API.Persistence;

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

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("IdentityDB")!);

var app = builder.Build();

app.MapCarter();

app.UseExceptionHandler(options => { });

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseHealthChecks("/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

await DataSeeder.SeedDataAsync(app.Services, app.Environment);

app.Run();

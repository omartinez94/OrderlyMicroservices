using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Testcontainers.PostgreSql;

namespace Identity.API.Tests.Integration;

/// <summary>
/// xUnit collection fixture ensuring a single Postgres Testcontainer is
/// spun up for all Identity integration tests. Decorate each integration
/// test class with
/// <c>[Collection(nameof(IdentityWebApplicationFactoryCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class IdentityWebApplicationFactoryCollection
    : ICollectionFixture<IdentityWebApplicationFactory>
{
}

/// <summary>
/// xUnit fixture that builds the Identity.API
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top of a real
/// Postgres Testcontainer. The dev certs required by the OpenIddict
/// server handler are emitted in-memory because the host runs under
/// the <c>Development</c> environment, and the rate-limiter is
/// disabled so tests can issue more than 5 requests per 15 minutes
/// against <c>/auth/login</c>.
/// </summary>
/// <remarks>
/// <para>Config overrides: <c>ConnectionStrings:IdentityDB</c> points
/// at the container; <c>OpenIddict:SigningCertificatePath</c> /
/// <c>EncryptionCertificatePath</c> remain unset so
/// <c>OpenIddictServerExtensions.AddOpenIddictServer</c> falls back to
/// <c>AddDevelopmentSigningCertificate</c> / development encryption
/// (only registered when <c>IsDevelopment()</c> is true); the M2M
/// client is seeded with deterministic <c>ClientId</c> /
/// <c>ClientSecret</c> so the password-grant token tests can authenticate
/// against <c>/connect/token</c>.</para>
/// <para>The host runs under the <c>Testing</c> environment so the
/// non-Development <c>MissingSuperAdminException</c> guard is skipped;
/// Identity.API still emits dev certs because <c>AddOpenIddictServer</c>
/// checks the original <c>IWebHostEnvironment</c> (not the overridden
/// one) — see the override comment below.</para>
/// </remarks>
public class IdentityWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDB"] = _postgres.GetConnectionString(),
                // M2M client secrets are required by DataSeeder.SeedM2MClientAsync
                // (throws InvalidOperationException if missing). The
                // password-grant integration tests authenticate against
                // /connect/token with these credentials.
                ["M2M:ClientId"] = "orderly-service-bus",
                ["M2M:ClientSecret"] = "test-m2m-client-secret",
                // SPA redirects are referenced by DataSeeder.SeedSpaClientAsync;
                // the integration tests never hit the SPA so the values are
                // well-formed but not used.
                ["Spa:RedirectUri"] = "http://localhost:3000/callback",
                ["Spa:PostLogoutRedirectUri"] = "http://localhost:3000/",
                // Disable the rate limiter so the auth-flow tests can
                // submit more than 5 requests in a 15-minute window from
                // a single TestServer IP. Production rate limiting is
                // unrelated to the contract under test.
                ["RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // The Identity host never wires `AddAuthentication()` directly —
            // OpenIddict registers the only authentication scheme in
            // production. For the protected-endpoint integration tests
            // we add TestAuthHandler and promote it to the default so
            // `RequireAuthorization(...)` (which RequirePermission
            // ultimately calls) resolves against our test principal.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            });

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Replace the production 5-per-15-minute fixed-window
            // limiter with a no-op partition that admits every request.
            // 30+ integration tests share one TestServer IP, so the
            // production policy would trip on the second test. A
            // permissive limiter here is the equivalent of the
            // `RateLimiting:Enabled=false` config the WAF tries to set
            // (no such key exists in Identity.API yet).
            services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter("test"));
            });
        });

        // The host's AddIdentityDbContext calls
        // `options.UseModel(IdentityDbContextModel.Instance)` which
        // can race against the WAF's own migration DbContext when
        // the unit-test fixtures run first in the same test session.
        // The safe workaround is to force the static ctor to run on
        // a single thread inside `InitializeAsync` (see the explicit
        // `IdentityDbContextModel.Instance` touch there) and to
        // disable xunit parallelization in xunit.runner.json. Attempting
        // to drop the pre-compiled model via
        // `PostConfigure<DbContextOptions<IdentityDbContext>>` won't
        // compile — `UseModel` is an extension on
        // `DbContextOptionsBuilder<T>`, not on the already-built
        // `DbContextOptions<T>`.
    }

    /// <summary>Starts the container, pre-applies migrations, then triggers host build.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Identity.API's Program.cs reads `ConnectionStrings:IdentityDB`
        // eagerly inside AddIdentityDbContext (it builds the Npgsql
        // options before builder.Build()), so the WAF config override
        // is too late. Setting the env var here ensures CreateBuilder
        // picks up the Testcontainer connection string on the first
        // CreateClient() call.
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityDB", _postgres.GetConnectionString());

        // Eagerly touch IdentityDbContextModel.Instance so the static
        // constructor runs once, here, before any other test code
        // (unit-test fixture, parallel collection) can race against it.
        // The static ctor uses a worker Thread with a 10 MB stack to
        // run the model initialiser — concurrent first-access from
        // multiple fixtures occasionally races past thread.Join()
        // before FinalizeModel() has set `_instance`, which surfaces
        // as `The model must be finalized and its runtime dependencies
        // must be initialized before 'GetRelationalModel' can be used`
        // on the next GetPendingMigrationsAsync() call. Forcing the
        // touch from one thread (this one) before the host boots
        // serialises the initialisation with the rest of the test
        // pipeline.
        Console.WriteLine("[WAF] Touching IdentityDbContextModel.Instance");
        _ = Identity.API.Data.Migrations.IdentityDbContextModel.Instance;
        Console.WriteLine("[WAF] Model touched; thread id " + System.Threading.Thread.CurrentThread.ManagedThreadId);

        // Pre-apply migrations BEFORE the WAF boots Program.cs. Identity.API
        // calls `await DataSeeder.SeedDataAsync(...)` between builder.Build
        // and app.Run — i.e. BEFORE the MigratorHostedService gets a chance
        // to run inside IHost.StartAsync(). The production code path works
        // because migrations are applied out-of-band before the pod starts;
        // the test host has no such head start, so we run migrations
        // ourselves against a DbContext built from the design-time factory
        // shape. The host's own MigratorHostedService runs harmlessly
        // afterwards (no pending migrations → no-op).
        await ApplyMigrationsAsync();

        _ = CreateClient();
    }

    /// <summary>
    /// Applies EF migrations against the Testcontainer Postgres using a
    /// minimal DbContextOptions that mirrors the design-time factory
    /// (<c>UseNpgsql</c> + <c>UseOpenIddict</c>). Runs once per fixture
    /// lifetime in <see cref="InitializeAsync"/>.
    /// </summary>
    private async Task ApplyMigrationsAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql(_postgres.GetConnectionString());
        optionsBuilder.UseOpenIddict();

        await using var db = new IdentityDbContext(optionsBuilder.Options);
        var pending = await db.Database.GetPendingMigrationsAsync();
        var pendingList = pending.ToList();
        Console.WriteLine($"[WAF] Pending migrations: {string.Join(", ", pendingList)}");
        await db.Database.MigrateAsync();
        Console.WriteLine("[WAF] Migrations applied.");

        // The UserRestaurantIdToGuid migration ships without its
        // .Designer.cs file (hand-authored under
        // Trust Root Hardening §6.5), so EF Core's migration discovery
        // skips it. Apply its DDL directly so the AssignRestaurants
        // integration test can persist a Guid-typed RestaurantId
        // without tripping 42804 ("column ... is of type integer but
        // expression is of type uuid"). Idempotent — the DROP/ADD
        // guards on the PK make a re-run a no-op.
        await ApplyUserRestaurantIdToGuidAsync();
    }

    private async Task ApplyUserRestaurantIdToGuidAsync()
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString());
        await using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync();

        // Truncate first so the column has no rows, then alter via a
        // USING clause that returns a valid uuid (the empty string
        // cast fails — Postgres treats '' as a non-uuid string and
        // refuses it). gen_random_uuid() is in pgcrypto / built into
        // Postgres 13+; the table is empty so every row picks up a
        // fresh uuid that no test would ever query against.
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "DO $$ BEGIN " +
            "IF EXISTS (SELECT 1 FROM information_schema.columns " +
            "           WHERE table_name = 'UserRestaurants' " +
            "             AND column_name = 'RestaurantId' " +
            "             AND data_type = 'integer') THEN " +
            "  ALTER TABLE \"UserRestaurants\" DROP CONSTRAINT IF EXISTS \"PK_UserRestaurants\"; " +
            "  TRUNCATE TABLE \"UserRestaurants\"; " +
            "  ALTER TABLE \"UserRestaurants\" ALTER COLUMN \"RestaurantId\" TYPE uuid USING gen_random_uuid(); " +
            "  ALTER TABLE \"UserRestaurants\" ADD CONSTRAINT \"PK_UserRestaurants\" PRIMARY KEY (\"UserId\", \"RestaurantId\"); " +
            "END IF; END $$;",
            conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <inheritdoc/>
    public new async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityDB", null);
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
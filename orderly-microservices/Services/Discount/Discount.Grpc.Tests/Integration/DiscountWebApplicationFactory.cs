using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.PostgreSql;
using MassTransit;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// xUnit collection fixture that builds the Discount.Grpc
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top of a real
/// PostgreSQL container managed by Testcontainers. Mirrors the
/// <see cref="Catalog.API.Tests.Integration.CatalogWebApplicationFactory"/>
/// pattern: the fixture owns the container lifecycle, the connection
/// string is injected as both in-memory config AND an env var (the env
/// var is what reaches the <c>NpgsqlDataSourceBuilder</c> at
/// <c>Program.cs</c> startup — Discount reads the connection string
/// eagerly, before <see cref="WebApplicationFactory{TEntryPoint}.ConfigureAppConfiguration"/>
/// runs).
/// </summary>
/// <remarks>
/// <para>Config overrides:</para>
/// <list type="bullet">
/// <item><c>ConnectionStrings:Database</c> → Testcontainer PG connection string.</item>
/// <item><c>Outbox:Enabled=false</c> skips the relay loop; circuit-breaker
/// and dead-letter tests drive <see cref="OutboxDispatcher{TContext}.DispatchOnceAsync"/>
/// directly.</item>
/// <item><c>IdentityServiceUrl=http://localhost:1</c> is unreachable —
/// <see cref="DiscountAuthorizationInterceptor"/> reads
/// <see cref="TestAuthHandler"/>'s claims, not Identity's JWT.</item>
/// <item><c>DiscountExpirySweep:Enabled=false</c> keeps the sweep service
/// from running during the test window; the expiry-sweep test invokes
/// the service directly with a fake clock.</item>
/// </list>
/// <para>The host runs under the <c>Testing</c> environment so the
/// <c>AspNetCore.HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse</c>
/// path is reachable via the standard middleware. Schema is applied by
/// <c>Program.cs</c>'s inline-await <c>MigrateAsync()</c> at host startup.</para>
/// </remarks>
public class DiscountWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private static readonly string[] EnvVarKeys =
    {
        "ConnectionStrings__Database",
        "Outbox__Enabled",
        "DiscountExpirySweep__Enabled",
        "IdentityServiceUrl",
        "Discount__IdempotencyKey",
    };

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:Enabled"] = "false",
                ["DiscountExpirySweep:Enabled"] = "false",
                ["IdentityServiceUrl"] = "http://localhost:1",
                // Idempotency-Key dev-only fallback kicks in (random per-process
                // key) when not provided. Tests that need a deterministic key
                // override this via WithSetting in the test body.
                ["Discount:IdempotencyKey"] = null,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            });

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.AddMassTransitTestHarness();
        });

        // Register the gRPC auth-bridge interceptor that closes the
        // ASP.NET Core gRPC gap on arbitrary Metadata → HttpContext.User
        // promotion. The Interceptors collection on GrpcServiceOptions is
        // populated by the production AddGrpc() in Program.cs, which now
        // adds DiscountAuthorizationInterceptor at the end of the chain.
        // We need TestGrpcAuthInterceptor (sets HttpContext.User) to run
        // FIRST so DiscountAuthorizationInterceptor sees a populated
        // principal when it calls AuthorizeAsync.
        //
        // PostConfigure<GrpcServiceOptions> runs AFTER every Configure
        // callback (including the production AddGrpc). The `Add<T>()`
        // extension appends, so we then move the registration to index 0
        // — the public ctor of InterceptorRegistration isn't available
        // in this grpc.core.api version, so we use the working
        // `Add<T>() + RemoveAt + Insert` dance instead.
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<GrpcServiceOptions>(options =>
            {
                options.Interceptors.Add<TestGrpcAuthInterceptor>();
                var lastIndex = options.Interceptors.Count - 1;
                var last = options.Interceptors[lastIndex];
                options.Interceptors.RemoveAt(lastIndex);
                options.Interceptors.Insert(0, last);
            });
        });
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        // Start the Postgres Testcontainer FIRST. The connection string
        // becomes available only after StartAsync() resolves.
        await _postgres.StartAsync();

        // Discount's Program.cs reads the connection string eagerly via
        // NpgsqlDataSourceBuilder, so WebApplicationFactory's
        // ConfigureAppConfiguration (applied later in the host pipeline)
        // is too late. Environment variables are read by
        // WebApplication.CreateBuilder at CreateClient() time, which
        // forces the host to build with the env-var values. Mirror
        // Catalog.API.Tests.Integration.CatalogWebApplicationFactory:108-118.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Database", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Outbox__Enabled", "false");
        Environment.SetEnvironmentVariable("DiscountExpirySweep__Enabled", "false");
        Environment.SetEnvironmentVariable("IdentityServiceUrl", "http://localhost:1");
        Environment.SetEnvironmentVariable("Discount__IdempotencyKey", null);

        // Trigger host build → NpgsqlDataSourceBuilder reads env vars →
        // inline-await MigrateAsync() applies the schema.
        _ = CreateClient();
    }

    /// <inheritdoc/>
    public new async Task DisposeAsync()
    {
        foreach (var key in EnvVarKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    /// Convenience helper for tests that want to seed a
    /// <see cref="DiscountContext"/> row directly via the production
    /// scope. Skips the global query filter for the inserted row (EF
    /// Core's <c>Add</c> bypasses query filters by design; reads still
    /// respect the filter).
    /// </summary>
    public async Task<DiscountContext> CreateDiscountContextAsync()
    {
        var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        // Detach from the scope lifecycle — caller uses it directly and
        // disposes via `await using` or via WithSeed below.
        return await Task.FromResult(db);
    }
}
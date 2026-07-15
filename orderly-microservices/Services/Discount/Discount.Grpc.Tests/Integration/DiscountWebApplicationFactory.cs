using Microsoft.AspNetCore.TestHost;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// xUnit collection fixture that builds the Discount.Grpc
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top of a per-fixture
/// SQLite file in the OS temp directory. No containers — Discount is
/// SQLite-based and the in-memory provider was rejected because EF Core's
/// <c>AuditableEntityInterceptor</c> runs on the SQLite provider only,
/// not on the in-memory provider.
/// </summary>
/// <remarks>
/// <para>
/// Config overrides:
/// </para>
/// <list type="bullet">
/// <item><c>ConnectionStrings:Database</c> points at a unique temp file
/// (<c>discountdb-test-{guid}.db</c> + <c>Cache=Shared</c>) so multiple
/// scopes (and the WebApplicationFactory's host process) share state.
/// The file is removed in <see cref="DisposeAsync"/>.</item>
/// <item><c>Outbox:Enabled=false</c> skips the relay loop; circuit-breaker
/// and dead-letter tests drive <see cref="OutboxDispatcher{TContext}.DispatchOnceAsync"/>
/// directly.</item>
/// <item><c>IdentityServiceUrl=http://localhost:1</c> is unreachable —
/// <see cref="Discount.Grpc.Authorization.DiscountAuthorizationInterceptor"/>
/// uses a <see cref="DiscountAuthorizationInterceptor"/> that reads
/// <c>TestAuthHandler</c>'s claims, not Identity's JWT.</item>
/// <item><c>DiscountExpirySweep:Enabled=false</c> keeps the sweep service
/// from running during the test window; the expiry-sweep test invokes
/// the service directly with a fake clock.</item>
/// </list>
/// <para>The host runs under the <c>Testing</c> environment so the
/// <c>AspNetCore.HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse</c>
/// path is reachable via the standard middleware.</para>
/// </remarks>
public class DiscountWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Per-fixture temp-file SQLite path. Each factory instance
    /// gets its own file; cleaned up in <see cref="DisposeAsync"/>.</summary>
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"discountdb-test-{Guid.NewGuid():N}.db");

    /// <summary>SQLite connection string with <c>Cache=Shared</c> so
    /// scopes within the same fixture see the same rows.</summary>
    public string ConnectionString => $"Data Source={DatabasePath};Cache=Shared";

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
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
        });

        // Register the gRPC auth-bridge interceptor that closes the
        // ASP.NET Core gRPC gap on arbitrary Metadata → HttpContext.User
        // promotion. Added here (rather than only in ConfigureTestServices)
        // because the Interceptors collection on GrpcServiceOptions is
        // configured at AddGrpc time. Order in the global pipeline:
        // TestGrpcAuthInterceptor (sets HttpContext.User) runs FIRST so
        // the existing DiscountAuthorizationInterceptor sees a populated
        // principal when it runs AuthorizeAsync.
        builder.ConfigureServices(services =>
        {
            services.AddGrpc(o =>
            {
                o.Interceptors.Add<TestGrpcAuthInterceptor>();
            });
        });
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        // Build the host; defer schema creation to the test's first
        // DbContext access via EnsureCreated(). We don't use Migrate()
        // because the .NET 9+ PendingModelChangesWarning check rejects
        // any model change not yet captured in a migration; that's the
        // production safety rail but for tests we just need a working
        // schema on the per-fixture SQLite file.
        _ = CreateClient();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        await db.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc/>
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        // Best-effort cleanup of the per-fixture SQLite file. Multiple
        // connections can hold the file open if a test leaked a scope;
        // swallow IOException so the test runner doesn't fail teardown.
        try
        {
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }
        }
        catch (IOException)
        {
            // Intentionally swallowed — file is per-fixture temporary storage.
        }
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

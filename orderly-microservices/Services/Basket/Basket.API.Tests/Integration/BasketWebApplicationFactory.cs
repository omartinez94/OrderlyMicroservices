using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Basket.API.Tests.Integration;

/// <summary>
/// xUnit collection fixture that spins up real Postgres + Redis +
/// RabbitMQ via Testcontainers, builds the Basket.API
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top, and
/// replaces the JWT bearer scheme with
/// <see cref="TestAuthHandler"/>. The real Postgres container is what
/// makes the multi-tenant Marten <c>MultiTenanted()</c> filter, the
/// expiry-sweep LINQ query, and the outbox dispatcher testable —
/// the in-memory store would bypass Npgsql and the production
/// configuration would never run.
/// </summary>
/// <remarks>
/// <para>Config overrides: the outbox dispatcher hosted service is
/// disabled (<c>Outbox:Enabled=false</c>) and the expiry sweep is
/// disabled (<c>Basket:ExpirySweep:Enabled=false</c>) so the polling
/// loops don't run during the test window — tests that need either
/// service invoke it directly (the expiry-sweep test re-enables the
/// service via <see cref="BasketExpirySweepWebApplicationFactory"/>).
/// The <c>OpenTelemetry:Enabled=false</c> flag keeps the test host
/// quiet (no OTLP exporter; the in-process spans are still emitted
/// to <see cref="System.Diagnostics.Activity.Current"/>).</para>
/// <para>The host runs under the <c>Testing</c> environment so
/// Basket's <c>UseSwagger</c> / <c>UseSwaggerUI</c> dev-only branches
/// are skipped; the JSON spec is still reachable through
/// <c>ISwaggerProvider.GetSwagger("v1")</c> from the test process.</para>
/// <para>Mirrors the shape of
/// <c>Catalog.API.Tests.Integration.CatalogWebApplicationFactory</c>
/// almost exactly. The connection-string env-var pre-load in
/// <see cref="InitializeAsync"/> is required because Basket's
/// <c>Program.cs:41</c> + <c>Program.cs:89</c> read
/// <c>ConnectionStrings:BasketDB</c> and the Redis multiplexer
/// eagerly before <see cref="WebApplicationFactory{TEntryPoint}"/>'s
/// <c>ConfigureAppConfiguration</c> runs.</para>
/// </remarks>
public class BasketWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Load the test-specific config file. The default config
            // chain already reads `appsettings.json` from the API
            // project (the project reference copies it to the test
            // bin directory). The Test.json overlay supplies the
            // empty connection-string placeholders + the
            // OpenTelemetry/Outbox/ExpirySweep disabling flags.
            config.AddJsonFile(
                "appsettings.Test.json",
                optional: true,
                reloadOnChange: false);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Unreachable authority — the JWT bearer scheme is replaced
                // by TestAuthHandler below, so metadata is never fetched.
                ["IdentityServiceUrl"] = "http://localhost:1",
                // Skip the outbox relay loop; checkout tests drive
                // `IDocumentSession` directly (the outbox dispatcher is
                // itself untested at the integration level — its
                // pure-handler tests already cover the staging path).
                ["Outbox:Enabled"] = "false",
                // Skip the expiry sweep loop; the sweep test invokes
                // the service directly with real wall clock against the
                // seeded expired basket.
                ["Basket:ExpirySweep:Enabled"] = "false",
                // Disable the OTLP exporter; the pipeline still builds
                // and the in-process Activity is still emitted.
                ["OpenTelemetry:Enabled"] = "false",
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
    }

    /// <summary>
    /// Starts the containers, then sets the env-var overrides
    /// (because Program.cs reads them eagerly), then triggers host
    /// build so <c>ApplyAllDatabaseChangesOnStartup</c> creates the
    /// per-tenant schemas.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _rabbit.StartAsync();

        // The WAF's ConfigureAppConfiguration runs LATE in the host
        // pipeline — too late for `var connectionString = ...` at
        // Program.cs:41 and too late for `ConnectionMultiplexer.Connect`
        // at Program.cs:89. Set the env vars before CreateClient() so
        // the eager reads in Program.cs see the Testcontainer values.
        Environment.SetEnvironmentVariable("ConnectionStrings__BasketDB", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("MessageBroker__Host", $"amqp://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}");
        Environment.SetEnvironmentVariable("MessageBroker__UserName", "guest");
        Environment.SetEnvironmentVariable("MessageBroker__Password", "guest");
        Environment.SetEnvironmentVariable("GrpcSettings__DiscountUrl", "http://localhost:1");
        Environment.SetEnvironmentVariable("IdentityServiceUrl", "http://localhost:1");

        _ = CreateClient();
    }

    /// <inheritdoc/>
    public new async Task DisposeAsync()
    {
        foreach (var key in new[]
        {
            "ConnectionStrings__BasketDB", "ConnectionStrings__Redis",
            "MessageBroker__Host", "MessageBroker__UserName", "MessageBroker__Password",
            "GrpcSettings__DiscountUrl", "IdentityServiceUrl",
        })
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _rabbit.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Subclass that re-enables the expiry-sweep hosted service. The
/// <c>BasketExpirySweepTests</c> collection uses this factory so the
/// <c>BasketExpirySweepService</c> starts polling the Marten store
/// (the test itself sets a fixed wall clock via the seeded basket
/// ages and the real <c>SystemClock.Instance</c>).
/// </summary>
public sealed class BasketExpirySweepWebApplicationFactory : BasketWebApplicationFactory
{
    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Basket:ExpirySweep:Enabled"] = "true",
            });
        });
    }
}

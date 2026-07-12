using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Catalog.API.Tests.Integration;

/// <summary>
/// xUnit collection fixture that spins up real Postgres + Redis + RabbitMQ
/// via Testcontainers, builds the Catalog.API
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top, and replaces the
/// JWT bearer scheme with <see cref="TestAuthHandler"/>. The real Postgres
/// container is what makes the job tests possible — the production
/// <c>AuditableEntityInterceptor</c> runs on the real Npgsql provider (the
/// in-memory provider bypassed it, which is why the job tests were
/// deferred).
/// </summary>
/// <remarks>
/// <para>Config overrides: the outbox dispatcher hosted service is disabled
/// (<c>Outbox:Enabled=false</c>) so the dead-letter test can drive a
/// single <c>DispatchOnceAsync</c> deterministically; Hangfire recurring-job
/// registration is disabled (<c>Catalog:Hangfire:Enabled=false</c>) so the
/// tests invoke <c>RunAsync</c> directly with a fake clock; and the
/// <c>CatalogScheduledJobs</c> feature flag is enabled so the real
/// <c>IFeatureManager</c> lets the jobs run.</para>
/// <para>The host runs under the <c>Testing</c> environment so Catalog's
/// Development-only Marten seed (<c>InitializeMartenWith&lt;CatalogInitialData&gt;</c>)
/// is skipped; the EF migration runner still applies the relational schema.</para>
/// </remarks>
public class CatalogWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDB"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                // A credential-free amqp://host:port — MassTransit supplies the
                // username/password separately, and Catalog's RabbitMQ health check
                // builds amqp://{user}:{pass}@{host} from it (an embedded-credential
                // URI from Testcontainers' GetConnectionString would double the creds
                // and produce an invalid URI).
                ["MessageBroker:Host"] = $"amqp://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}",
                ["MessageBroker:UserName"] = "guest",
                ["MessageBroker:Password"] = "guest",
                // Unreachable authority — the JWT bearer scheme is replaced by
                // TestAuthHandler below, so metadata is never fetched.
                ["IdentityServiceUrl"] = "http://localhost:1",
                // Skip the outbox relay loop; tests drive DispatchOnceAsync directly.
                ["Outbox:Enabled"] = "false",
                // Skip Hangfire recurring-job registration; tests invoke RunAsync directly.
                ["Catalog:Hangfire:Enabled"] = "false",
                // Enable the scheduled-jobs gate so the real IFeatureManager lets jobs run.
                ["FeatureManagement:CatalogScheduledJobs"] = "true",
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

    /// <summary>Starts the containers, then triggers host build so Program.cs's migration runner applies the schema.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _rabbit.StartAsync();

        // Catalog's Program.cs reads these connection strings EAGERLY (it builds an
        // NpgsqlDataSource and configures Marten/Hangfire before builder.Build()), so
        // WebApplicationFactory's ConfigureAppConfiguration — applied later in the host
        // pipeline — is too late. Environment variables are read during
        // WebApplication.CreateBuilder (which runs at the CreateClient() call below,
        // after the containers are up), so they reliably override appsettings here.
        Environment.SetEnvironmentVariable("ConnectionStrings__CatalogDB", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("MessageBroker__Host", $"amqp://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}");
        Environment.SetEnvironmentVariable("MessageBroker__UserName", "guest");
        Environment.SetEnvironmentVariable("MessageBroker__Password", "guest");
        Environment.SetEnvironmentVariable("IdentityServiceUrl", "http://localhost:1");
        Environment.SetEnvironmentVariable("Outbox__Enabled", "false");
        Environment.SetEnvironmentVariable("Catalog__Hangfire__Enabled", "false");
        Environment.SetEnvironmentVariable("FeatureManagement__CatalogScheduledJobs", "true");

        _ = CreateClient();
    }

    /// <inheritdoc/>
    public new async Task DisposeAsync()
    {
        foreach (var key in new[]
        {
            "ConnectionStrings__CatalogDB", "ConnectionStrings__Redis",
            "MessageBroker__Host", "MessageBroker__UserName", "MessageBroker__Password",
            "IdentityServiceUrl", "Outbox__Enabled", "Catalog__Hangfire__Enabled",
            "FeatureManagement__CatalogScheduledJobs",
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

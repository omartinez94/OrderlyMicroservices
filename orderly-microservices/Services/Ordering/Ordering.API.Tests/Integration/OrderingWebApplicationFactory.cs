using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

namespace Ordering.API.Tests.Integration;

/// <summary>
/// xUnit collection fixture that spins up a real MSSQL + RabbitMQ via
/// Testcontainers, builds the Ordering.API
/// <see cref="WebApplicationFactory{TEntryPoint}"/> on top, and replaces
/// the JWT auth scheme with <see cref="TestAuthHandler"/>. Tests
/// authenticate by sending <c>X-Test-User</c> + <c>X-Test-Permissions</c>
/// headers; requests without those headers fall through and the endpoint
/// authorization middleware returns 401.
/// </summary>
public class OrderingWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _mssql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("YourStrong!Passw0rd")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string MsSqlConnectionString => _mssql.GetConnectionString();
    public string RabbitMqConnectionString => _rabbit.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Pass the full AMQP URI straight through; MassTransit's
            // AddMessageBroker reads MessageBroker:Host and uses it as-is.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = MsSqlConnectionString,
                ["MessageBroker:Host"] = RabbitMqConnectionString,
                ["MessageBroker:UserName"] = "guest",
                ["MessageBroker:Password"] = "guest",
                // Point at an unreachable host so the JWT bearer scheme
                // (which is no longer the default in tests) never reaches
                // out to the real Identity authority.
                ["IdentityServiceUrl"] = "http://localhost:1",
                // Disable the outbox dispatcher hosted service in tests —
                // the publisher still writes to outbox_messages when
                // aggregate mutations land, but nothing relays. Keeps the
                // round-trip tests free from a noisy background loop.
                ["Outbox:Enabled"] = "false",
                // Keep the feature flag off so OrderCreatedEventHandler
                // doesn't try to publish on every seed.
                ["FeatureManagement:OrderFullfilment"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the default Bearer scheme with the test scheme so
            // the pipeline short-circuits before JWT validation runs.
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

    public async Task InitializeAsync()
    {
        await _mssql.StartAsync();
        await _rabbit.StartAsync();

        // Trigger the host to build so Program.cs's migration runner
        // applies the schema.
        _ = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await _mssql.DisposeAsync();
        await _rabbit.DisposeAsync();
        await base.DisposeAsync();
    }
}

using Kitchen.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Kitchen.API.Tests.Integration;

/// <summary>
/// xUnit collection fixture that spins up a real Postgres + RabbitMQ via
/// Testcontainers, builds the Kitchen.API <see cref="WebApplicationFactory{TEntryPoint}"/>
/// on top, and replaces the JWT auth scheme with
/// <see cref="TestAuthHandler"/>. Tests authenticate by sending
/// <c>X-Test-User</c> + <c>X-Test-Permissions</c> headers; requests without
/// those headers fall through and the endpoint authorization middleware
/// returns 401.
/// </summary>
public class KitchenWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kitchendb_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
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
                ["ConnectionStrings:KitchenDB"] = PostgresConnectionString,
                ["MessageBroker:Host"] = RabbitMqConnectionString,
                ["MessageBroker:UserName"] = "guest",
                ["MessageBroker:Password"] = "guest",
                // Point at an unreachable host so the JWT bearer scheme
                // (which is no longer the default in tests) never reaches
                // out to the real Identity authority.
                ["IdentityServiceUrl"] = "http://localhost:1",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the default Bearer scheme with the test scheme so the
            // pipeline short-circuits before JWT validation runs. The test
            // handler only authenticates when X-Test-User is set; otherwise
            // it returns NoResult and RequireAuthorization returns 401.
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
        await _postgres.StartAsync();
        await _rabbit.StartAsync();

        // Trigger the host to build so Program.cs's migration runner runs.
        _ = CreateClient();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<KitchenDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
        await base.DisposeAsync();
    }

    private static string ExtractAmqpHost(string amqpConnectionString)
    {
        // Helper kept for backwards-compat — the host config now passes the
        // full AMQP URI through.
        var uri = new Uri(amqpConnectionString);
        return $"amqp://{uri.Host}:{uri.Port}";
    }
}
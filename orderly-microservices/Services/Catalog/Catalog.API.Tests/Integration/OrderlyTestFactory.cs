using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace Catalog.API.Tests.Integration;

public class OrderlyTestFactory<TProgram> : WebApplicationFactory<TProgram>, IAsyncLifetime where TProgram : class
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDB"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
                ["MessageBroker:Host"] = $"amqp://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}",
                ["MessageBroker:UserName"] = "guest",
                ["MessageBroker:Password"] = "guest",
                ["IdentityServiceUrl"] = "http://localhost:1",
                ["Outbox:Enabled"] = "false",
                ["Catalog:Hangfire:Enabled"] = "false",
                ["FeatureManagement:CatalogScheduledJobs"] = "true",
                ["JWT_SECRET"] = AuthTokenHelper.TestJwtSecret
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _rabbit.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__CatalogDB", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("MessageBroker__Host", $"amqp://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}");
        Environment.SetEnvironmentVariable("MessageBroker__UserName", "guest");
        Environment.SetEnvironmentVariable("MessageBroker__Password", "guest");
        Environment.SetEnvironmentVariable("IdentityServiceUrl", "http://localhost:1");
        Environment.SetEnvironmentVariable("Outbox__Enabled", "false");
        Environment.SetEnvironmentVariable("Catalog__Hangfire__Enabled", "false");
        Environment.SetEnvironmentVariable("FeatureManagement__CatalogScheduledJobs", "true");
        Environment.SetEnvironmentVariable("JWT_SECRET", AuthTokenHelper.TestJwtSecret);

        _ = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        foreach (var key in new[]
        {
            "ConnectionStrings__CatalogDB", "ConnectionStrings__Redis",
            "MessageBroker__Host", "MessageBroker__UserName", "MessageBroker__Password",
            "IdentityServiceUrl", "Outbox__Enabled", "Catalog__Hangfire__Enabled",
            "FeatureManagement__CatalogScheduledJobs", "JWT_SECRET"
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

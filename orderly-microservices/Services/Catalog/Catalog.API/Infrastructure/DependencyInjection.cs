using BuildingBlocks.Messaging.MassTransit;
using BuildingBlocks.Messaging.Outbox;
using Catalog.API.Infrastructure.Interceptors;
using System.Reflection;

namespace Catalog.API.Infrastructure;

/// <summary>
/// Composition root for Catalog's infrastructure layer: outbox options,
/// outbox publisher (scoped), and the outbox dispatcher hosted service
/// (singleton, gated by <see cref="OutboxOptions.Enabled"/>).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Catalog's messaging + outbox infrastructure. Call from
    /// <c>Program.cs</c> after <c>AddFeatureManagement()</c> and before
    /// <c>AddCarter()</c> so the consumer assembly is included in the
    /// MassTransit scan.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Outbox options binding — `Outbox:Enabled`, `Outbox:ActivePollInterval`, etc.
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        // Outbox publisher — scoped so each request gets its own instance and
        // piggybacks on the ambient CatalogDbContext change tracker (the
        // publisher adds rows directly; the same SaveChanges that persists
        // the aggregate mutation persists the outbox row).
        services.AddScoped<IOutboxPublisher, CatalogOutboxPublisher>();
        services.AddScoped<CatalogOutboxPublisher>();

        // MassTransit + RabbitMQ — discovers consumers in the Catalog assembly
        // (the OrderCompletedIntegrationEventHandler lives here in Phase 2).
        services.AddMessageBroker(configuration, Assembly.GetExecutingAssembly());

        // Outbox dispatcher hosted service — gated so tests flip
        // `Outbox:Enabled=false` to skip the relay loop (mirrors
        // Ordering.Infrastructure/DependencyInjection.cs lines 24-30).
        var outboxEnabled = configuration.GetValue(
            $"{OutboxOptions.SectionName}:Enabled", true);
        if (outboxEnabled)
        {
            services.AddHostedService<CatalogOutboxDispatcher>();
        }

        return services;
    }
}
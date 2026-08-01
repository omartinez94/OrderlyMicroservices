using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ordering.Application.Data;
using Ordering.Infrastructure.Data.Interceptors;
using Ordering.Infrastructure.Persistence;
using Ordering.Infrastructure.Services;

namespace Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        // Outbox: the publisher is scoped (one per request), the dispatcher
        // is a singleton hosted service. Tests flip OutboxOptions.Enabled
        // = false to skip the dispatcher registration entirely.
        services.AddScoped<BuildingBlocks.Messaging.Outbox.IOutboxPublisher, OrderingOutboxPublisher>();
        services.AddScoped<OrderingOutboxPublisher>();

        services.AddDbContext<ApplicationDBContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions
                    // EnableRetryOnFailure enabled project-wide
                    // The outbox dispatcher's
                    // BeginTransactionAsync is wrapped in
                    // Database.CreateExecutionStrategy().ExecuteAsync(...)
                    // (BuildingBlocks.Messaging/Outbox/OutboxDispatcher.cs:148).
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null));
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDBContext>());

        // Replace the dev-only inline MigrateWithRetryAsync
        // (Ordering.Infrastructure/Data/Extensions/DatabaseExtensions.cs)
        // with the shared MigratorHostedService. The hosted service runs
        // at IHostedService.StartAsync with exponential-backoff retry on
        // MSSQL transient SqlException numbers (1801, 4060, 40613, 233,
        // -2) — surviving the 60-90s MSSQL cold-init window.
        services.Configure<MigratorHostedServiceOptions>(
            configuration.GetSection(MigratorHostedServiceOptions.SectionName));
        services.AddHostedService<OrderingMigratorHostedService>();

        var outboxEnabled = configuration.GetValue(
            $"{OutboxOptions.SectionName}:Enabled", true);
        if (outboxEnabled)
        {
            services.AddHostedService<OrderingOutboxDispatcher>();
            // IOrderingOutboxRunner handle for the dev-only
            // /_dev/trigger/outbox-relay endpoint. Resolved against the
            // same singleton hosted service so the dev endpoint and the
            // periodic timer share the broker-circuit-breaker state.
            services.AddSingleton<IOrderingOutboxRunner>(sp =>
                sp.GetRequiredService<OrderingOutboxDispatcher>());
        }

        // IDailyReconciliationRunner handle for /_dev/trigger/daily-reconciliation.
        services.AddSingleton<IDailyReconciliationRunner, DailyReconciliationRunner>();

        return services;
    }
}
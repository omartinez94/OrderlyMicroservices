using BuildingBlocks.Entities.Interceptors;
using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ordering.Application.Data;
using Ordering.Infrastructure.Data.Interceptors;

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
            options.UseSqlServer(connectionString);
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDBContext>());

        var outboxEnabled = configuration.GetValue(
            $"{OutboxOptions.SectionName}:Enabled", true);
        if (outboxEnabled)
        {
            services.AddHostedService<OrderingOutboxDispatcher>();
        }

        return services;
    }
}
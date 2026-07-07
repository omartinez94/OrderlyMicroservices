using BuildingBlocks.Messaging.Outbox;
using Kitchen.API.Infrastructure.Data;
using Kitchen.API.Infrastructure.Interceptors;

namespace Kitchen.API.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires the Kitchen relational store + repositories + transactional
    /// outbox. The <see cref="DispatchDomainEventsInterceptor"/> drains
    /// aggregate <c>DomainEvents</c> after every commit so application
    /// handlers run in-process; the <see cref="KitchenOutboxDispatcher"/>
    /// relays staged <c>outbox_messages</c> rows onto the broker in the
    /// background (Phase C).
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<KitchenDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<SaveChangesInterceptor>());
            options.UseNpgsql(
                configuration.GetConnectionString("KitchenDB")!,
                npgsqlOptions => npgsqlOptions.UseNodaTime());
        });

        services.AddScoped<IKitchenTicketRepository, KitchenTicketRepository>();
        services.AddScoped<IKitchenStationRepository, KitchenStationRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<KitchenDbContext>());

        // Outbox: publisher is scoped (one per request), dispatcher is a
        // singleton hosted service. Tests flip OutboxOptions.Enabled =
        // false to skip the dispatcher registration entirely.
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<IOutboxPublisher, KitchenOutboxPublisher>();
        services.AddScoped<KitchenOutboxPublisher>();

        var outboxEnabled = configuration.GetValue(
            $"{OutboxOptions.SectionName}:Enabled", true);
        if (outboxEnabled)
        {
            services.AddHostedService<KitchenOutboxDispatcher>();
        }

        return services;
    }
}
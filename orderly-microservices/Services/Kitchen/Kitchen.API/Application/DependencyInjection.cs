using System.Reflection;

namespace Kitchen.API.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Wires application-layer services: MediatR + open behaviors (validation
    /// + logging, mirroring the other services) and the messaging broker
    /// (MassTransit + RabbitMQ). The handlers register automatically through
    /// assembly scanning — new queries/commands and <c>IConsumer&lt;T&gt;</c>
    /// implementations in this assembly are picked up on startup.
    /// </summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddMessageBroker(configuration, Assembly.GetExecutingAssembly());

        return services;
    }
}
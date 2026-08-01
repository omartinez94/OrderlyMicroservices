using BuildingBlocks.Messaging.Exceptions;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace BuildingBlocks.Messaging.MassTransit;

public static class Extensions
{
    public static IServiceCollection AddMessageBroker(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null)
    {
        // Defensively validate every required MessageBroker
        // key. Previously `new Uri(configuration["MessageBroker:Host"]!)`
        // threw ArgumentNullException with no diagnostic about which key
        // was missing — operators had to guess. BrokerConfigurationException
        // enumerates every absent key in one message.
        var missing = new List<string>();
        var host = configuration["MessageBroker:Host"];
        var userName = configuration["MessageBroker:UserName"];
        var password = configuration["MessageBroker:Password"];
        if (string.IsNullOrWhiteSpace(host))
            missing.Add("MessageBroker:Host");
        if (string.IsNullOrWhiteSpace(userName))
            missing.Add("MessageBroker:UserName");
        if (string.IsNullOrWhiteSpace(password))
            missing.Add("MessageBroker:Password");
        if (missing.Count > 0)
        {
            throw new BrokerConfigurationException(
                $"Missing required MessageBroker configuration keys: {string.Join(", ", missing)}.",
                missing);
        }

        services.AddMassTransit(config =>
        {
            config.SetKebabCaseEndpointNameFormatter();

            if (assembly != null)
                config.AddConsumers(assembly);

            config.UsingRabbitMq((context, configurator) =>
            {
                configurator.Host(new Uri(host!), hostCfg =>
                {
                    hostCfg.Username(userName!);
                    hostCfg.Password(password!);
                });

                configurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}

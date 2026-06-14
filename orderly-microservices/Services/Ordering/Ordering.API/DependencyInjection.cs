using BuildingBlocks.Exceptions.Handler;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Ordering.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddJwtAuthentication(
            authority: configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
            audience: "OrderlyMicroservices");
        services.AddAuthorizationServices();

        services.AddHttpContextAccessor();
        services.AddCarter();

        services.AddExceptionHandler<CustomExceptionHandler>();

        services.AddHealthChecks()
            .AddSqlServer(configuration.GetConnectionString("Database")!);

        return services;
    }

    public static WebApplication UseApiServices(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapCarter();

        app.UseExceptionHandler(opts => { });

        app.UseHealthChecks("/health",
            new HealthCheckOptions
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            }
        );

        return app;
    }
}

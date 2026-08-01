using BuildingBlocks.Dev;
using BuildingBlocks.Exceptions.Handler;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Ordering.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        services.AddJwtAuthenticationWithDevFallback(
            environment,
            configuration,
            authority: configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
            audience: "OrderlyMicroservices");
        services.AddAuthorizationServices();

        services.AddHttpContextAccessor();
        services.AddCarter();

        // Dev-only runner handles for the Orderly.DevMCP.Server's
        // `trigger_scheduled_jobs` tool. The interfaces live in
        // Ordering.Infrastructure so the Infrastructure project can
        // implement them without a circular dep. Concrete registration
        // happens in Ordering.Infrastructure.DependencyInjection.
        // Here we just expose the endpoints; the runner-resolve is
        // scoped-per-request by the dev endpoint handler.

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

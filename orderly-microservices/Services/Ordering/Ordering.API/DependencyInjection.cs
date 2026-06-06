using Carter;

namespace Ordering.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddJwtAuthentication(
            authority: configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5057",
            audience: "OrderlyMicroservices");

        services.AddAuthorizationServices();

        services.AddCarter();

        return services;
    }

    public static WebApplication UseApiServices(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCarter();
        return app;
    }
}

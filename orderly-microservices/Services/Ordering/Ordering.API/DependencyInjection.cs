namespace Ordering.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddJwtAuthentication(
            authority: configuration.GetValue<string>("IdentityServiceUrl") ?? "https://localhost:5007",
            audience: "OrderlyMicroservices");

        services.AddAuthorizationServices();

        return services;
    }

    public static WebApplication UseApiServices(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

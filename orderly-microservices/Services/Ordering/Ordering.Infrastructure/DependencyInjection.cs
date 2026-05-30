using BuildingBlocks.Entities.Interceptors;

namespace Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<ApplicationDBContext>(options =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(new AuditableEntityInterceptor());
        });

        return services;
    }
}

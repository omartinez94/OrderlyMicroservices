using Kitchen.API.Infrastructure.Interceptors;

namespace Kitchen.API.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires the Kitchen relational store + repositories. The
    /// <see cref="DispatchDomainEventsInterceptor"/> drains aggregate
    /// <c>DomainEvents</c> after every commit so application handlers run
    /// in-process.
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

        return services;
    }
}
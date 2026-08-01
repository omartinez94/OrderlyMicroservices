using BuildingBlocks.Entities.Interceptors;
using Identity.API.Data.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Identity.API.Extensions;

public static class IdentityDbContextExtensions
{
    public static IServiceCollection AddIdentityDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

        services.AddDbContext<Data.IdentityDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("IdentityDB")!,
                npgsqlOptions => npgsqlOptions
                    // Phase 2: EnableRetryOnFailure enabled project-wide
                    // (plan §6.1). Identity has no outbox dispatcher, so
                    // no ExecutionStrategy wrapping is needed here.
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null));
            options.UseOpenIddict();
            options.UseModel(IdentityDbContextModel.Instance);
            options.AddInterceptors(new AuditableEntityInterceptor());
        });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<Data.IdentityDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }
}

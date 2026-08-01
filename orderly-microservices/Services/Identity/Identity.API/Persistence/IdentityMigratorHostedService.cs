using BuildingBlocks.Persistence;
using Identity.API.Data;

namespace Identity.API.Persistence;

/// <summary>
/// Concrete <see cref="MigratorHostedService{TContext}"/> for Identity. Resolves
/// a fresh <see cref="IdentityDbContext"/> from the per-attempt scope so the
/// migration runner doesn't share a tracked context with the OpenIddict
/// application/scope managers or the seeder.
/// </summary>
public sealed class IdentityMigratorHostedService(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<MigratorHostedServiceOptions> options,
    Microsoft.Extensions.Logging.ILogger<IdentityMigratorHostedService> logger)
    : MigratorHostedService<IdentityDbContext>(services, options, logger)
{
    protected override IdentityDbContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<IdentityDbContext>();
}
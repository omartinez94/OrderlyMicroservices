using BuildingBlocks.Persistence;

namespace Catalog.API.Persistence;

/// <summary>
/// Concrete <see cref="MigratorHostedService{TContext}"/> for Catalog. Resolves
/// a fresh <see cref="CatalogDbContext"/> from the per-attempt scope so the
/// migration runner doesn't share a tracked context with the Carter /
/// Hangfire / Marten handlers.
/// </summary>
public sealed class CatalogMigratorHostedService(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<MigratorHostedServiceOptions> options,
    Microsoft.Extensions.Logging.ILogger<CatalogMigratorHostedService> logger)
    : MigratorHostedService<CatalogDbContext>(services, options, logger)
{
    protected override CatalogDbContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<CatalogDbContext>();
}
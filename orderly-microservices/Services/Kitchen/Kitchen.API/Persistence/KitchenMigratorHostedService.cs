using BuildingBlocks.Persistence;
using Kitchen.API.Infrastructure.Data;

namespace Kitchen.API.Persistence;

/// <summary>
/// Concrete <see cref="MigratorHostedService{TContext}"/> for Kitchen. Resolves
/// a fresh <see cref="KitchenDbContext"/> from the per-attempt scope so the
/// migration runner doesn't share a tracked context with the Carter /
/// SignalR / MassTransit consumer handlers.
/// </summary>
public sealed class KitchenMigratorHostedService(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<MigratorHostedServiceOptions> options,
    Microsoft.Extensions.Logging.ILogger<KitchenMigratorHostedService> logger)
    : MigratorHostedService<KitchenDbContext>(services, options, logger)
{
    protected override KitchenDbContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<KitchenDbContext>();
}
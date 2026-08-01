using BuildingBlocks.Persistence;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// Concrete <see cref="MigratorHostedService{TContext}"/> for Ordering. Resolves
/// a fresh <see cref="ApplicationDBContext"/> from the per-attempt scope so
/// the migration runner doesn't share a tracked context with the MediatR
/// command handlers. Phase 2 supersedes the dev-only
/// <c>Ordering.Infrastructure/Data/Extensions/DatabaseExtensions.MigrateWithRetryAsync</c>
/// with this generic hosted service — same retry semantics, single source
/// of truth.
/// </summary>
public sealed class OrderingMigratorHostedService(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<MigratorHostedServiceOptions> options,
    Microsoft.Extensions.Logging.ILogger<OrderingMigratorHostedService> logger)
    : MigratorHostedService<ApplicationDBContext>(services, options, logger)
{
    protected override ApplicationDBContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<ApplicationDBContext>();
}
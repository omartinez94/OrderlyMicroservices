using BuildingBlocks.Persistence;
using Discount.Grpc.Data;

namespace Discount.Grpc.Persistence;

/// <summary>
/// Concrete <see cref="MigratorHostedService{TContext}"/> for Discount. Resolves
/// a fresh <see cref="DiscountContext"/> from the per-attempt scope so the
/// migration runner doesn't share a tracked context with the gRPC handlers.
/// </summary>
public sealed class DiscountMigratorHostedService(
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<MigratorHostedServiceOptions> options,
    Microsoft.Extensions.Logging.ILogger<DiscountMigratorHostedService> logger)
    : MigratorHostedService<DiscountContext>(services, options, logger)
{
    protected override DiscountContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<DiscountContext>();
}
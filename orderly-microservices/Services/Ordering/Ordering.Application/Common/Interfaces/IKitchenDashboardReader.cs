namespace Ordering.Application.Common.Interfaces;

using Ordering.Application.Dashboard.Dtos;

public record KitchenVelocityResult(
    KitchenVelocitySnapshotDto? KitchenVelocity,
    string SectionStatus);

public interface IKitchenDashboardReader
{
    Task<KitchenVelocityResult> GetVelocityAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}

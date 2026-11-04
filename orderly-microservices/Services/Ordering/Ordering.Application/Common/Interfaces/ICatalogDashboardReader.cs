namespace Ordering.Application.Common.Interfaces;

using Ordering.Application.Dashboard.Dtos;

public record CatalogFloorPulseResult(
    FloorPulseSnapshotDto? FloorPulse,
    IReadOnlyList<WaitlistEntryDto> Waitlist,
    string? Currency,
    string SectionStatus);

public interface ICatalogDashboardReader
{
    Task<CatalogFloorPulseResult> GetFloorPulseAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}

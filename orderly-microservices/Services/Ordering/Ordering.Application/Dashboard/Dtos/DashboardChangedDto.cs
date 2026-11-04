namespace Ordering.Application.Dashboard.Dtos;

using NodaTime;

public record DashboardChangedDto(
    Guid RestaurantId,
    IReadOnlyList<string> ChangedSections,
    Instant OccurredAt);

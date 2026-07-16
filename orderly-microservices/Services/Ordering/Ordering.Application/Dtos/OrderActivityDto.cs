using Ordering.Domain.Enums;

namespace Ordering.Application.Dtos;

/// <summary>
/// Wire-shape projection of an <see cref="OrderActivity"/> domain row.
/// Read-only — the activity feed is appended to inside the aggregate and
/// never edited via the API.
/// </summary>
/// <param name="Id">The activity row id (Guid).</param>
/// <param name="ActivityType">Closed enum value (one of
/// <see cref="OrderActivityType"/>).</param>
/// <param name="ActorUserId">Optional Guid reference of the user who
/// triggered the transition; <c>null</c> for kitchen-driven /
/// system-driven transitions.</param>
/// <param name="OccurredAt">UTC instant the activity was recorded.</param>
/// <param name="CorrelationId">The ambient request/bus correlation id
/// (stamped from <c>BuildingBlocks.Correlation.CorrelationContext.Current</c>);
/// <c>null</c> when no request/bus scope produced the transition.</param>
/// <param name="Notes">Optional free-text reason (today only the
/// cancellation reason uses this).</param>
/// <param name="Metadata">Typed status-transition snapshot
/// (<see cref="OrderActivityMetadata"/>); populated per the transition
/// callout table.</param>
public record OrderActivityDto(
    Guid Id,
    OrderActivityType ActivityType,
    Guid? ActorUserId,
    Instant OccurredAt,
    string? CorrelationId,
    string? Notes,
    OrderActivityMetadata? Metadata);
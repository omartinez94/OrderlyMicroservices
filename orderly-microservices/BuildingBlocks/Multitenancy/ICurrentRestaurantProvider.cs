namespace BuildingBlocks.Multitenancy;

/// <summary>
/// Provides the current restaurant (tenant) identifier for the active request or
/// event-processing scope. Implementations include:
///   - <see cref="ClaimsRestaurantProvider"/> for HTTP / gRPC request scope (Phase 1+3).
///   - A bus-event-derived synthetic provider (Pattern 2 from Q10) for MassTransit consumers (Phase 5).
/// Returns <see cref="Guid.Empty"/> when no tenant can be resolved; the global query
/// filter then matches no rows, which is the fail-secure default.
/// </summary>
public interface ICurrentRestaurantProvider
{
    Guid RestaurantId { get; }
}

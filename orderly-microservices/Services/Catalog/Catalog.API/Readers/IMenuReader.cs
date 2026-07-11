namespace Catalog.API.Readers;

/// <summary>
/// Read-side abstraction over the menu tree. The catalog cache wires the
/// Scrutor <c>CachedMenuReader</c> decorator over the concrete
/// <see cref="MenuReader"/> via <c>services.Decorate&lt;IMenuReader, CachedMenuReader&gt;()</c>
/// in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// Returns a <see cref="MenuSnapshot"/> (not EF Core entities) so the cache
/// payload is a stable, schema-independent shape and downstream services can
/// consume it directly without re-projecting.
/// </remarks>
public interface IMenuReader
{
    /// <summary>
    /// Assembles the full menu tree for a restaurant: categories → sub-categories →
    /// items (with variations and ingredient requirements).
    /// </summary>
    /// <param name="restaurantId">The restaurant's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A populated <see cref="MenuSnapshot"/> or <see langword="null"/> when the
    /// restaurant has no menu (no non-deleted categories). The cached
    /// decorator treats <see langword="null"/> as a miss-eligible result and
    /// does not store it.
    /// </returns>
    Task<MenuSnapshot?> GetByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);
}
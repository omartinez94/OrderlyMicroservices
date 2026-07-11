namespace Catalog.API.Models;

/// <summary>
/// Per-restaurant stock toggle for a recipe component. The Ingredient
/// Availability Engine recomputes every menu item that references this
/// ingredient when <see cref="IsAvailable"/> flips.
/// </summary>
/// <remarks>
/// Changed the base from <c>AuditableEntity&lt;int&gt;</c> to
/// <c>AuditableAggregate&lt;int&gt;</c> so the entity carries
/// <c>DomainEvents</c> for the
/// <c>DispatchDomainEventsInterceptor</c> to drain. Audit columns are
/// preserved (the <c>AuditableAggregate</c> base extends
/// <c>AuditableEntity</c>) so <c>AuditableEntityInterceptor</c> continues
/// to stamp <c>LastModifiedAt</c> without a schema migration.
/// </remarks>
public class Ingredient : AuditableAggregate<int>
{
    public decimal CurrentStock { get; set; }
    public bool IsAvailable { get; set; }
    public decimal MinimumStock { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    /// <summary>Unit of measurement: kg, liters, units, etc.</summary>
    public string Unit { get; set; } = string.Empty;
}

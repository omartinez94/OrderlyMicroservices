namespace Catalog.API.Models;

/// <summary>
/// Junction row between a menu item and an ingredient (a recipe line).
/// The Ingredient Availability Engine reads these to compute
/// <c>MenuItem.AvailabilityStatus</c>.
/// </summary>
/// <remarks>
/// Changed the base from <c>Entity&lt;int&gt;</c> to <c>Aggregate&lt;int&gt;</c>
/// so the entity carries <c>DomainEvents</c> for the
/// <c>DispatchDomainEventsInterceptor</c> to drain. The base class is
/// <c>Aggregate</c> (not <c>AuditableAggregate</c>) because this entity has
/// no audit columns — it inherits the bare <c>Entity&lt;int&gt;</c> via
/// <c>Aggregate&lt;TId&gt;</c>.
/// </remarks>
public class MenuItemIngredient : Aggregate<int>
{
    public int IngredientId { get; set; }
    public bool IsOptional { get; set; }
    public Guid MenuItemId { get; set; }
    public decimal QuantityRequired { get; set; }
}

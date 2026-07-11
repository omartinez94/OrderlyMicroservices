namespace Catalog.API.Models;

/// <summary>
/// Original→alternative mapping (e.g. <c>Lettuce → Spinach</c>). When
/// <c>OriginalIngredientId</c> is out of stock, the engine consults the
/// alternatives to decide whether the menu item stays
/// <c>Limited</c> or flips to <c>Unavailable</c>.
/// </summary>
/// <remarks>
/// Changed the base from <c>Entity&lt;int&gt;</c> to <c>Aggregate&lt;int&gt;</c>
/// so the entity carries <c>DomainEvents</c> for the
/// <c>DispatchDomainEventsInterceptor</c> to drain. The base class is
/// <c>Aggregate</c> (not <c>AuditableAggregate</c>) because this entity has
/// no audit columns — it inherits the bare <c>Entity&lt;int&gt;</c> via
/// <c>Aggregate&lt;TId&gt;</c>.
/// </remarks>
public class IngredientAlternative : Aggregate<int>
{
    public int AlternativeIngredientId { get; set; }
    public bool AutoSubstitute { get; set; }
    public int OriginalIngredientId { get; set; }
    /// <summary>Price adjustment when this alternative is used (e.g. +$1.00 for gluten-free bun)</summary>
    public decimal PriceModifier { get; set; }
    public Guid RestaurantId { get; set; }
}

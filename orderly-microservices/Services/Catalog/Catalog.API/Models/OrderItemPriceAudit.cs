namespace Catalog.API.Models;

/// <summary>
/// Marten document capturing the per-item price breakdown captured at order time.
/// Marten assigns a synthetic <see cref="Guid"/> id (no relational base class).
/// </summary>
public class OrderItemPriceAudit
{
    /// <summary>Marten synthetic primary key.</summary>
    public Guid Id { get; set; }
    public decimal AppliedBasePrice { get; set; }
    public Instant CapturedAt { get; set; }
    public Guid CapturedByUserId { get; set; }
    public string CustomizationsBreakdown { get; set; } = string.Empty;
    public decimal CustomizationsPriceTotal { get; set; }
    public decimal DiscountApplied { get; set; }
    public string DiscountSource { get; set; } = string.Empty;
    public decimal FinalUnitPrice { get; set; }
    public decimal MenuBasePrice { get; set; }
    public Guid MenuItemId { get; set; }
    public int OrderItemId { get; set; }
    public string VariationsBreakdown { get; set; } = string.Empty;
    public decimal VariationsPriceTotal { get; set; }
}

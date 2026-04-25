namespace Catalog.API.Models;

public class OrderItemPriceAudit : Entity<int>
{
    public int OrderItemId { get; set; }
    public Guid MenuItemId { get; set; }
    public decimal MenuBasePrice { get; set; }
    public decimal AppliedBasePrice { get; set; }
    public string VariationsBreakdown { get; set; } = string.Empty;
    public decimal VariationsPriceTotal { get; set; }
    public string CustomizationsBreakdown { get; set; } = string.Empty;
    public decimal CustomizationsPriceTotal { get; set; }
    public decimal FinalUnitPrice { get; set; }
    public decimal DiscountApplied { get; set; }
    public string DiscountSource { get; set; } = string.Empty;
    public Guid CapturedByUserId { get; set; }
    public Instant CapturedAt { get; set; }
}

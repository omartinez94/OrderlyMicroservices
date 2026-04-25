namespace Catalog.API.Models;

public class OrderSnapshot : Entity<int>
{
    public Guid OrderId { get; set; }
    /// <summary>Complete order snapshot serialized as JSON</summary>
    public string FullOrderData { get; set; } = string.Empty;
    
    /// <summary>JSON snapshot of all active menu prices when the order was created</summary>
    public string MenuPricesSnapshot { get; set; } = string.Empty;
    
    /// <summary>JSON snapshot of the active tax rules</summary>
    public string TaxConfiguration { get; set; } = string.Empty;
    
    /// <summary>JSON snapshot of applied discount rules</summary>
    public string DiscountRules { get; set; } = string.Empty;
    public string GeneratedReceiptHtml { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
}

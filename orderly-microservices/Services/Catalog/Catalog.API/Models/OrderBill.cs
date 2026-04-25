namespace Catalog.API.Models;

public class OrderBill : Entity<int>
{
    public Guid OrderId { get; set; }
    public int BillNumber { get; set; }
    /// <summary>How the bill was split: equal, custom</summary>
    public SplitType SplitType { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>Payment state: pending, paid, void</summary>
    public PaymentStatus PaymentStatus { get; set; }
    
    /// <summary>Payment method used: cash, card, transfer</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public Instant? PaidAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
}

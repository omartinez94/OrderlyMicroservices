namespace Catalog.API.Models;

public class OrderBill : Entity<int>
{
    public decimal Amount { get; set; }
    public int BillNumber { get; set; }
    public Instant CreatedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    /// <summary>Payment method used: cash, card, transfer</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    /// <summary>Payment state: pending, paid, void</summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    /// <summary>How the bill was split: equal, custom</summary>
    public SplitType SplitType { get; set; } = SplitType.Equal;
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public Instant? PaidAt { get; set; }
}

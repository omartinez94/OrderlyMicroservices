namespace Catalog.API.Models;

/// <summary>
/// Marten document capturing every modification made to an order.
/// Marten assigns a synthetic <see cref="Guid"/> id (no relational base class).
/// </summary>
public class OrderModificationLog
{
    /// <summary>Marten synthetic primary key.</summary>
    public Guid Id { get; set; }
    public Instant CreatedAt { get; set; }
    /// <summary>Type of modification: status_change, item_added, item_removed, price_change, manager_override</summary>
    public string ModificationType { get; set; } = string.Empty;
    public Guid ModifiedByUserId { get; set; }
    /// <summary>Complete JSON snapshot of the state after the change</summary>
    public string NewData { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    /// <summary>Complete JSON snapshot of the state before the change</summary>
    public string PreviousData { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    /// <summary>JSON document describing price differences applied during modification</summary>
    public string PriceImpact { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public Instant? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
}

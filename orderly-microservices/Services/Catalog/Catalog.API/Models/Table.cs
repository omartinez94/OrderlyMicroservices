namespace Catalog.API.Models;

public class Table : AuditableEntity<Guid>
{
    public int Capacity { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public Guid RestaurantId { get; set; }
    public string Shape { get; set; } = string.Empty;
    public TableStatus Status { get; set; } = TableStatus.Available;
    public string TableNumber { get; set; } = string.Empty;
    public Guid? CurrentOrderId { get; set; }
}

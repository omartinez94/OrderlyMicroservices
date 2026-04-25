namespace Catalog.API.Models;

public class Restaurant : AuditableEntity<Guid>
{
    public string Address { get; set; } = string.Empty;
    public bool AllowAutoSubstitute { get; set; } = false;
    public bool AutoConfirmOrders { get; set; } = false;
    public bool AutoConfirmReservations { get; set; } = false;
    public Guid BrandId { get; set; }
    public string Currency { get; set; } = "MXN";
    public string Email { get; set; } = string.Empty;
    public int EstimatedTurnoverMinutes { get; set; } = 30;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal TaxRate { get; set; } = 0.0m;
    public string TimeZone { get; set; } = "UTC";
}

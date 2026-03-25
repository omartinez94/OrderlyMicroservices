using BuildingBlocks.Entities.Contracts;

namespace Catalog.API.Models;

public class Restaurant : Entity<Guid>
{
    public int BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal TaxRate { get; set; } = 0.0m;
    public string Currency { get; set; } = "MXN";
    public string TimeZone { get; set; } = "UTC";
    public bool AutoConfirmOrders { get; set; } = false;
    public bool AutoConfirmReservations { get; set; } = false;
    public bool AllowAutoSubstitute { get; set; } = false;
    public int EstimatedTurnoverMinutes { get; set; } = 30;
}

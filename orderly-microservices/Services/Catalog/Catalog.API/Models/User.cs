namespace Catalog.API.Models;

public class User : AuditableEntity<Guid>
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Admin,Manager,Waiter,KitchenStaff
    public Guid RestaurantId { get; set; }
}

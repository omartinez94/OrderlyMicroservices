namespace Catalog.API.Models;

public class User : AuditableEntity<Guid>
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public Role Role { get; set; }
}

namespace Ordering.Domain.Models;

public class Customer : AuditableEntity<Guid>
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

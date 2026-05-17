namespace Ordering.Domain.Models;

public class Customer : AuditableEntity<CustomerId>
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Address? Address { get; set; }

    public static Customer Create(CustomerId customerId, string email, string name, string phone, Address? address = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(email));
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        return new Customer
        {
            Id = customerId,
            Email = email,
            Name = name,
            Phone = phone,
            Address = address
        };
    }
}

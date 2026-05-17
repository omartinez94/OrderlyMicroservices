namespace Ordering.Domain.Models;

public class MenuItem : Abstractions::Entity<MenuItemId>
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public static MenuItem Create(MenuItemId id, string name, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        return new MenuItem
        {
            Id = id,
            Name = name,
            Price = price
        };
    }
}

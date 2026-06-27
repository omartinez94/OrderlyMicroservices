namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers <see cref="MenuItem.Create"/>. The menu-item factory's only guard is the
/// non-empty-name rule, but it's worth pinning because menu items are referenced by
/// <see cref="OrderItem"/> snapshots and stale names corrupt historical order data.
/// </summary>
public sealed class MenuItemTests
{
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    /// <summary>
    /// Happy path: id, name, and price round-trip through the factory unchanged.
    /// </summary>
    [Fact]
    public void Create_WithValidArgs_ReturnsMenuItemWithIdAndFields()
    {
        var id = NewMenuItemId();
        const string name = "Cheeseburger";
        const decimal price = 9.99m;

        var menuItem = MenuItem.Create(id, name, price);

        menuItem.Id.Should().Be(id);
        menuItem.Name.Should().Be(name);
        menuItem.Price.Should().Be(price);
    }

    /// <summary>
    /// Name guard: null, empty, and whitespace are all rejected. Snapshots are taken
    /// at order time, so a missing name would surface on every receipt for that item.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceName_Throws(string? name)
    {
        Action act = () => MenuItem.Create(NewMenuItemId(), name!, 9.99m);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }
}
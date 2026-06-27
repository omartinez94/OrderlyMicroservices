namespace Ordering.Domain.Tests.ValueObjects;

/// <summary>
/// Covers the strongly-typed <see cref="MenuItemId"/> wrapper. Same pattern as
/// <see cref="CustomerIdTests"/>: the wrapper exists to keep <see cref="Guid.Empty"/>
/// out of the catalog of menu items the system thinks it knows about.
/// </summary>
public sealed class MenuItemIdTests
{
    /// <summary>
    /// Happy path: any non-empty Guid round-trips through the wrapper.
    /// </summary>
    [Fact]
    public void Of_WithNonEmptyGuid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();

        var menuItemId = MenuItemId.Of(guid);

        menuItemId.Value.Should().Be(guid);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is rejected. Without this guard an <c>OrderItem</c>
    /// could be created with a meaningless menu-item reference.
    /// </summary>
    [Fact]
    public void Of_WithEmptyGuid_Throws()
    {
        Action act = () => MenuItemId.Of(Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("Domain exception: MenuItemId cannot be empty. throws from Domain Layer. (Parameter: value)*");
    }
}
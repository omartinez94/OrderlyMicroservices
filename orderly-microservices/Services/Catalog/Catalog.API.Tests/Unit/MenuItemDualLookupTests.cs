using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using Catalog.API.Features.MenuItems.GetMenuItemById;
using Catalog.API.Models;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Unit;

public sealed class MenuItemDualLookupTests
{
    [Fact]
    public void MenuItem_ImplementsIShortIdentifiable()
    {
        MenuItem menuItem = new()
        {
            Id = Guid.NewGuid(),
            ShortId = "itm5p9w2x8k",
            Name = "Burger"
        };

        menuItem.Should().BeAssignableTo<IShortIdentifiable>();
        menuItem.ShortId.Should().Be("itm5p9w2x8k");
    }

    [Fact]
    public void GetMenuItemByIdQuery_WithGuid_PopulatesIdentifierAndIdProperty()
    {
        Guid guid = Guid.NewGuid();
        GetMenuItemByIdQuery query = new(guid);

        query.Identifier.Should().Be(guid.ToString());
        query.Id.Should().Be(guid);
    }

    [Fact]
    public void GetMenuItemByIdQuery_WithShortId_PopulatesIdentifierAndEmptyGuid()
    {
        string shortId = "itm5p9w2x8k";
        GetMenuItemByIdQuery query = new(shortId);

        query.Identifier.Should().Be(shortId);
        query.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void MenuItem_ShortIdGenerator_GeneratesCompliantPrefix()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.MenuItem);

        shortId.Should().StartWith(EntityPrefixConstants.MenuItem);
        shortId.Length.Should().Be(11);
        ShortIdGenerator.IsValid(shortId, EntityPrefix.MenuItem).Should().BeTrue();
    }
}

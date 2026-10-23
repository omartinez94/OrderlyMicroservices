using BuildingBlocks.Entities.Interfaces;
using BuildingBlocks.Identifiers;
using Catalog.API.Exceptions;
using Catalog.API.Features.Restaurants.GetRestaurantById;
using Catalog.API.Models;
using FluentAssertions;
using Xunit;

namespace Catalog.API.Tests.Unit;

public sealed class RestaurantDualLookupTests
{
    [Fact]
    public void Restaurant_ImplementsIShortIdentifiable()
    {
        Restaurant restaurant = new()
        {
            Id = Guid.NewGuid(),
            ShortId = "rstk8j3x9mq",
            Name = "Test"
        };

        restaurant.Should().BeAssignableTo<IShortIdentifiable>();
        restaurant.ShortId.Should().Be("rstk8j3x9mq");
    }

    [Fact]
    public void GetRestaurantByIdQuery_WithGuid_PopulatesIdentifierAndIdProperty()
    {
        Guid guid = Guid.NewGuid();
        GetRestaurantByIdQuery query = new(guid);

        query.Identifier.Should().Be(guid.ToString());
        query.Id.Should().Be(guid);
    }

    [Fact]
    public void GetRestaurantByIdQuery_WithShortId_PopulatesIdentifierAndEmptyGuid()
    {
        string shortId = "rstk8j3x9mq";
        GetRestaurantByIdQuery query = new(shortId);

        query.Identifier.Should().Be(shortId);
        query.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void RestaurantNotFoundException_WithGuid_FormatsCorrectMessage()
    {
        Guid guid = Guid.NewGuid();
        RestaurantNotFoundException exception = new(guid);

        exception.Message.Should().Contain("Restaurant");
        exception.Message.Should().Contain(guid.ToString());
    }

    [Fact]
    public void RestaurantNotFoundException_WithShortId_FormatsCorrectMessage()
    {
        string shortId = "rstk8j3x9mq";
        RestaurantNotFoundException exception = new(shortId);

        exception.Message.Should().Contain("Restaurant");
        exception.Message.Should().Contain(shortId);
    }

    [Fact]
    public void Restaurant_ShortIdGenerator_GeneratesCompliantPrefix()
    {
        string shortId = ShortIdGenerator.Generate(EntityPrefix.Restaurant);

        shortId.Should().StartWith(EntityPrefixConstants.Restaurant);
        shortId.Length.Should().Be(11);
        ShortIdGenerator.IsValid(shortId, EntityPrefix.Restaurant).Should().BeTrue();
    }
}

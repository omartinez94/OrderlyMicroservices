using System.Security.Claims;
using BuildingBlocks.Authorization;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Tests.Unit;

public class JwtClaimExtensionsTests
{
    [Fact]
    public void HasRestaurantAccess_WhenSuperAdmin_ReturnsTrueForAnyRestaurant()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "SuperAdmin")
        ]));

        principal.HasRestaurantAccess(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void HasRestaurantAccess_WhenMatchingRestaurantId_ReturnsTrue()
    {
        Guid restaurantId = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", restaurantId.ToString())
        ]));

        principal.HasRestaurantAccess(restaurantId).Should().BeTrue();
        principal.HasRestaurantAccess(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void HasRestaurantAccess_WhenMatchingRestaurant_IdsSnakeCase_ReturnsTrue()
    {
        Guid r1 = Guid.NewGuid();
        Guid r2 = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurant_ids", r1.ToString()),
            new Claim("restaurant_ids", r2.ToString())
        ]));

        principal.HasRestaurantAccess(r1).Should().BeTrue();
        principal.HasRestaurantAccess(r2).Should().BeTrue();
        principal.HasRestaurantAccess(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void HasRestaurantAccess_WhenMatchingRestaurantIdsCamelCase_ReturnsTrue()
    {
        Guid r1 = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantIds", r1.ToString())
        ]));

        principal.HasRestaurantAccess(r1).Should().BeTrue();
    }

    [Fact]
    public void GetRestaurantIds_ExtractsAndDeduplicatesAllVariations()
    {
        Guid r1 = Guid.NewGuid();
        Guid r2 = Guid.NewGuid();
        ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim("restaurantId", r1.ToString()),
            new Claim("restaurant_ids", r1.ToString()),
            new Claim("restaurantIds", r2.ToString())
        ]));

        var ids = principal.GetRestaurantIds();

        ids.Should().HaveCount(2);
        ids.Should().Contain([r1, r2]);
    }
}

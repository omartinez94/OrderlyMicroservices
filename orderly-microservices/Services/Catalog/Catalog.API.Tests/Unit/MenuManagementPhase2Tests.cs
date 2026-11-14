using BuildingBlocks.Exceptions;
using Catalog.API.Application.Abstractions;

namespace Catalog.API.Tests.Unit;

public sealed class MenuManagementPhase2Tests
{
    private sealed class StubCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; }

        public Guid? UserId { get; set; }

        public Guid? RestaurantId { get; set; }

        public IReadOnlyList<Guid> RestaurantIds { get; set; } = [];

        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    [Fact]
    public void EnsureTenantAccess_WhenCurrentUserIsNull_DoesNotThrow()
    {
        ICurrentUser? user = null;
        Guid restaurantId = Guid.NewGuid();

        Action act = () => user.EnsureTenantAccess(restaurantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTenantAccess_WhenCurrentUserHasNoClaims_DoesNotThrow()
    {
        StubCurrentUser user = new()
        {
            IsAuthenticated = false,
            UserId = null,
            RestaurantId = null,
            Roles = [],
        };
        Guid restaurantId = Guid.NewGuid();

        Action act = () => user.EnsureTenantAccess(restaurantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTenantAccess_WhenUserIsSuperAdmin_AllowsAnyRestaurantId()
    {
        StubCurrentUser user = new()
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            RestaurantId = null, // SuperAdmin has no default restaurant
            Roles = ["SuperAdmin"],
        };
        Guid restaurantId = Guid.NewGuid();

        Action act = () => user.EnsureTenantAccess(restaurantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTenantAccess_WhenUserBelongsToMatchingRestaurant_AllowsAccess()
    {
        Guid restaurantId = Guid.NewGuid();
        StubCurrentUser user = new()
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Roles = ["Manager"],
        };

        Action act = () => user.EnsureTenantAccess(restaurantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTenantAccess_WhenUserHasTargetInRestaurantIdsList_AllowsAccess()
    {
        Guid defaultRestaurantId = Guid.NewGuid();
        Guid targetRestaurantId = Guid.NewGuid();
        StubCurrentUser user = new()
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            RestaurantId = defaultRestaurantId,
            RestaurantIds = [defaultRestaurantId, targetRestaurantId],
            Roles = ["RestaurantAdmin"],
        };

        Action act = () => user.EnsureTenantAccess(targetRestaurantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureTenantAccess_WhenUserBelongsToDifferentRestaurant_ThrowsForbiddenException()
    {
        Guid userRestaurantId = Guid.NewGuid();
        Guid targetRestaurantId = Guid.NewGuid();
        StubCurrentUser user = new()
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            RestaurantId = userRestaurantId,
            Roles = ["Manager"],
        };

        Action act = () => user.EnsureTenantAccess(targetRestaurantId);

        act.Should().Throw<ForbiddenException>()
            .WithMessage("*not authorized*");
    }

    [Fact]
    public void EnsureTenantAccess_WhenUserHasNoRestaurantClaimAndNotSuperAdmin_ThrowsForbiddenException()
    {
        StubCurrentUser user = new()
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            RestaurantId = null,
            Roles = ["Waiter"],
        };
        Guid targetRestaurantId = Guid.NewGuid();

        Action act = () => user.EnsureTenantAccess(targetRestaurantId);

        act.Should().Throw<ForbiddenException>()
            .WithMessage("*not authorized*");
    }
}

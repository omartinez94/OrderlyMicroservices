namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="BasketIdentityGuardBehavior{TRequest,TResponse}"/>.
/// Exercises the identity cross-check (JWT vs. command's UserId/RestaurantId)
/// without spinning up the full pipeline — the behaviour is registered as
/// an open-generic MediatR behaviour in <c>Program.cs</c>, so the focused
/// unit test is the cheapest way to lock the contract.
/// </summary>
public sealed class BasketIdentityGuardBehaviorTests
{
    [Fact]
    public async Task RequestImplementingIdentity_MatchingUserAndRestaurant_PassesThrough()
    {
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var accessor = BuildHttpContextAccessor(userId, restaurantId);
        var behavior = new BasketIdentityGuardBehavior<SampleQuery, SampleResult>(accessor);
        var nextCalled = false;

        await behavior.Handle(
            new SampleQuery(userId, restaurantId),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(new SampleResult());
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task OtherUser_ReturnsForbidden()
    {
        var jwtUserId = Guid.NewGuid();
        var jwtRestaurantId = Guid.NewGuid();
        var requestUserId = Guid.NewGuid(); // different user

        var accessor = BuildHttpContextAccessor(jwtUserId, jwtRestaurantId);
        var behavior = new BasketIdentityGuardBehavior<SampleQuery, SampleResult>(accessor);

        var act = async () => await behavior.Handle(
            new SampleQuery(requestUserId, jwtRestaurantId),
            _ => Task.FromResult(new SampleResult()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CrossTenant_ReturnsForbidden()
    {
        var userId = Guid.NewGuid();
        var jwtRestaurantId = Guid.NewGuid();
        var requestRestaurantId = Guid.NewGuid(); // different tenant

        var accessor = BuildHttpContextAccessor(userId, jwtRestaurantId);
        var behavior = new BasketIdentityGuardBehavior<SampleQuery, SampleResult>(accessor);

        var act = async () => await behavior.Handle(
            new SampleQuery(userId, requestRestaurantId),
            _ => Task.FromResult(new SampleResult()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UnauthenticatedRequest_ThrowsForbiddenException()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext()); // no User

        var behavior = new BasketIdentityGuardBehavior<SampleQuery, SampleResult>(accessor);

        var act = async () => await behavior.Handle(
            new SampleQuery(Guid.NewGuid(), Guid.NewGuid()),
            _ => Task.FromResult(new SampleResult()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task RequestNotImplementingIdentity_PassesThrough()
    {
        // The guard must not interfere with non-basket requests that
        // happen to flow through the same pipeline (e.g. a future
        // admin endpoint that uses a different request shape).
        var accessor = BuildHttpContextAccessor(Guid.NewGuid(), Guid.NewGuid());
        var behavior = new BasketIdentityGuardBehavior<NonBasketQuery, NonBasketResult>(accessor);
        var nextCalled = false;

        await behavior.Handle(
            new NonBasketQuery(),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(new NonBasketResult());
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    private static IHttpContextAccessor BuildHttpContextAccessor(Guid userId, Guid restaurantId)
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier,
                userId.ToString()),
            new System.Security.Claims.Claim("restaurantId", restaurantId.ToString()),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private sealed record SampleQuery(Guid UserId, Guid RestaurantId) : IQuery<SampleResult>, IBasketIdentityRequest;
    private sealed record SampleResult;

    private sealed record NonBasketQuery : IQuery<NonBasketResult>;
    private sealed record NonBasketResult;
}
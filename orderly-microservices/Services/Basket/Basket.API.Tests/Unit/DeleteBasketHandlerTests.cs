using Basket.API.Basket.DeleteBasket;

namespace Basket.API.Tests.Unit;

/// <summary>
/// Unit-level coverage for <see cref="DeleteBasketHandler"/>. Locks
/// the Phase 3 contract:
///
/// <list type="bullet">
///   <item>
///     The handler returns <see cref="Unit.Value"/> on both the
///     "cart exists" and "cart already absent" paths (per §0.4.3
///     "204 No Content" semantics).
///   </item>
///   <item>
///     The handler does NOT depend on the boolean the inner
///     repository returned under the old
///     <c>DeleteBasketResult.IsSuccess</c> shape — the side effect
///     (cache invalidation) lives inside
///     <c>CachedBasketRepository.DeleteBasketAsync</c> when the
///     inner repository actually deletes a row.
///   </item>
///   <item>
///     The handler propagates identity through the
///     <c>BasketIdentityGuardBehavior</c> via the
///     <see cref="IBasketIdentityRequest"/> marker — covered by the
///     shared basket identity tests in
///     <c>BasketIdentityGuardBehaviorTests</c>.
///   </item>
/// </list>
/// </summary>
public sealed class DeleteBasketHandlerTests
{
    [Fact]
    public async Task ExistingCart_ReturnsUnit()
    {
        // Arrange — the inner repository reports a successful delete
        // (the boolean is captured by the CachedBasketRepository
        // decorator; the handler ignores it).
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var repository = Substitute.For<IBasketRepository>();
        repository
            .DeleteBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var handler = new DeleteBasketHandler(repository);

        // Act
        var result = await handler.Handle(
            new DeleteBasketCommand(userId, restaurantId),
            CancellationToken.None);

        // Assert — Unit.Value, not a new DeleteBasketResult instance.
        result.Should().Be(MediatR.Unit.Value);
    }

    [Fact]
    public async Task AbsentCart_ReturnsUnit_AndPropagatesCancellation()
    {
        // Arrange — the inner repository returns false (the cart was
        // already gone). The handler must still return Unit and
        // forward the cancellation token.
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var repository = Substitute.For<IBasketRepository>();
        repository
            .DeleteBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var handler = new DeleteBasketHandler(repository);

        using var cts = new CancellationTokenSource();

        // Act
        var result = await handler.Handle(
            new DeleteBasketCommand(userId, restaurantId),
            cts.Token);

        // Assert
        result.Should().Be(MediatR.Unit.Value);
        await repository
            .Received(1)
            .DeleteBasketAsync(userId, restaurantId, cts.Token);
    }

    [Fact]
    public async Task CancellationRequested_Propagates()
    {
        // Arrange — the cancellation token is already cancelled. The
        // handler must raise OperationCanceledException promptly;
        // CachedBasketRepository.DeleteBasketAsync would otherwise
        // reach into Marten and the cancellation would be observed
        // at the database boundary.
        var userId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var repository = Substitute.For<IBasketRepository>();
        repository
            .DeleteBasketAsync(userId, restaurantId, Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ =>
            {
                // Mimic the inner repository's cancellation contract.
                throw new OperationCanceledException();
            });

        var handler = new DeleteBasketHandler(repository);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new DeleteBasketCommand(userId, restaurantId), cts.Token));
    }
}

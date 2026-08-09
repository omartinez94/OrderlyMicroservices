using Ordering.Application.Orders.Commands.DeleteOrder;

namespace Ordering.Application.Tests.Commands;

/// <summary>
/// Covers <see cref="DeleteOrderHandler"/>: looks the order up via
/// <c>FindAsync</c>, calls <c>Orders.Remove</c>, persists, and returns
/// success. Negative path: unknown id throws <see cref="OrderNotFoundException"/>.
/// </summary>
/// <remarks>
/// This is a hard delete — child rows (OrderItems, Activities) are removed
/// via cascade (see <c>Ordering.Infrastructure/Data/Configurations/OrderConfiguration.cs</c>).
/// </remarks>
public sealed class DeleteOrderHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedPendingOrder()
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_ExistingOrder_RemovesAndSaves()
    {
        var order = SeedPendingOrder();
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders
            .FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(order));

        var handler = new DeleteOrderHandler(dbContext);
        var result = await handler.Handle(
            new DeleteOrderCommand(order.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dbContext.Orders.Received(1).Remove(order);
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders
            .FindAsync(Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Order?>(null));

        var handler = new DeleteOrderHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new DeleteOrderCommand(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }
}

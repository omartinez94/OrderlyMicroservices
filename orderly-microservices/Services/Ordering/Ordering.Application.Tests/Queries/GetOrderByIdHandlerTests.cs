using Ordering.Application.Orders.Queries.GetOrderById;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

/// <summary>
/// Covers <see cref="GetOrderByIdHandler"/>: single-result lookup via
/// <c>SingleOrDefaultAsync</c> over the in-memory provider. Negative
/// path: unknown id throws <see cref="OrderNotFoundException"/>.
/// </summary>
public sealed class GetOrderByIdHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedOrder()
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());
        order.Add(NewMenuItemId(), quantity: 1, price: 9.99m);
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_ExistingOrder_ReturnsDto()
    {
        var order = SeedOrder();
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(new[] { order }));

        var handler = new GetOrderByIdHandler(dbContext);
        var result = await handler.Handle(
            new GetOrderByIdQuery(order.Id.Value),
            CancellationToken.None);

        result.Order.Should().NotBeNull();
        result.Order.Id.Should().Be(order.Id.Value);
        result.Order.OrderNumber.Should().Be("ORD-2026-0001");
        result.Order.OrderItems.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(Array.Empty<Order>()));

        var handler = new GetOrderByIdHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new GetOrderByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }
}

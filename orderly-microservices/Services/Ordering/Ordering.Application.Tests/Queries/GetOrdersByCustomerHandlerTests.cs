using Ordering.Application.Orders.Queries.GetOrdersByCustomer;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

/// <summary>
/// Covers <see cref="GetOrdersByCustomerHandler"/>: filters by
/// <c>CustomerId</c> at the LINQ layer and orders by
/// <c>OrderNumber.Value</c>. Empty result is a valid 200 — the handler
/// does NOT throw on no-matches (see plan §Phase 3 implementation note).
/// </summary>
public sealed class GetOrdersByCustomerHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedOrderForCustomer(CustomerId customerId, string orderNumber)
    {
        var order = Order.Create(
            NewOrderId(),
            customerId,
            OrderNumber.Of(orderNumber),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());
        order.Add(NewMenuItemId(), quantity: 1, price: 9.99m);
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public async Task Handle_OrdersForTargetCustomer_ReturnsFilteredList()
    {
        var targetCustomerId = CustomerId.Of(Guid.NewGuid());
        var otherCustomerId = CustomerId.Of(Guid.NewGuid());

        var orders = new[]
        {
            SeedOrderForCustomer(targetCustomerId, "ORD-2026-0001"),
            SeedOrderForCustomer(otherCustomerId,   "ORD-2026-0002"),
            SeedOrderForCustomer(targetCustomerId, "ORD-2026-0003"),
            SeedOrderForCustomer(otherCustomerId,   "ORD-2026-0004"),
        };

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersByCustomerHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersByCustomerQuery(targetCustomerId.Value),
            CancellationToken.None);

        result.Orders.Should().HaveCount(2);
        result.Orders.Select(o => o.CustomerId).Should().AllBeEquivalentTo(targetCustomerId.Value);
    }

    [Fact]
    public async Task Handle_NoOrdersForTargetCustomer_ReturnsEmptyList()
    {
        var targetCustomerId = CustomerId.Of(Guid.NewGuid());
        var otherCustomerId = CustomerId.Of(Guid.NewGuid());

        var orders = new[]
        {
            SeedOrderForCustomer(otherCustomerId, "ORD-2026-0001"),
            SeedOrderForCustomer(otherCustomerId, "ORD-2026-0002"),
        };

        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersByCustomerHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersByCustomerQuery(targetCustomerId.Value),
            CancellationToken.None);

        result.Orders.Should().BeEmpty();
    }
}

using BuildingBlocks.Pagination;
using Ordering.Application.Orders.Queries.GetOrders;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

/// <summary>
/// Covers <see cref="GetOrdersHandler"/>: long-counts, paginates, orders
/// by <c>OrderNumber</c>, projects via <see cref="OrderExtensions.ToOrderDtoList"/>.
/// Negative path: empty store returns empty page + zero count.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="MockDbSet.From{TEntity}"/> for the first time in this
/// project — the existing <c>Orders.FindAsync(...)</c> substitute pattern
/// can't execute the <c>LongCountAsync</c> / <c>ToListAsync</c> /
/// <c>Skip</c> / <c>Take</c> pipeline.
/// </para>
/// <para>
/// <b>Caller contract reminder</b>: <c>Include</c> is a no-op on the
/// in-memory provider; aggregates must be seeded with their navigation
/// collections populated (call <c>Order.Add(menuItemId, qty, price)</c>
/// before constructing the test list).
/// </para>
/// </remarks>
public sealed class GetOrdersHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedOrder(string orderNumber)
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
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
    public async Task Handle_SeededOrders_ReturnsPaginatedDtos()
    {
        var orders = new[]
        {
            SeedOrder("ORD-2026-0001"),
            SeedOrder("ORD-2026-0002"),
            SeedOrder("ORD-2026-0003"),
        };
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersQuery(new PaginationRequest(0, 10)),
            CancellationToken.None);

        result.Orders.Count.Should().Be(3);
        result.Orders.PageIndex.Should().Be(0);
        result.Orders.PageSize.Should().Be(10);
        result.Orders.Data.Should().HaveCount(3);
        result.Orders.Data.Select(d => d.OrderNumber).Should().Contain(
            "ORD-2026-0001", "ORD-2026-0002", "ORD-2026-0003");
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsCorrectSlice()
    {
        var orders = Enumerable.Range(1, 25)
            .Select(i => SeedOrder($"ORD-2026-{i:0000}"))
            .ToArray();
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersQuery(new PaginationRequest(1, 10)),
            CancellationToken.None);

        result.Orders.Count.Should().Be(25);
        result.Orders.PageIndex.Should().Be(1);
        result.Orders.PageSize.Should().Be(10);
        result.Orders.Data.Should().HaveCount(10);
    }

    [Fact]
    public async Task Handle_EmptyStore_ReturnsEmptyResult()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(Array.Empty<Order>()));

        var handler = new GetOrdersHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersQuery(new PaginationRequest(0, 10)),
            CancellationToken.None);

        result.Orders.Count.Should().Be(0);
        result.Orders.Data.Should().BeEmpty();
    }
}

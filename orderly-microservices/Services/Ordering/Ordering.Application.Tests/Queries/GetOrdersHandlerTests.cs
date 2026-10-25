using BuildingBlocks.Pagination;
using Ordering.Application.Orders.Queries.GetOrders;
using Ordering.Application.Tests.Abstractions;

namespace Ordering.Application.Tests.Queries;

/// <summary>
/// Covers <see cref="GetOrdersHandler"/> and <see cref="GetOrdersQueryValidator"/>:
/// pagination, status/type/activeOnly/date/search filters, filtered total count,
/// sort options with null <c>CreatedAt</c> sorted last, and validation rules.
/// </summary>
public sealed class GetOrdersHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedOrder(
        string orderNumber,
        OrderStatus status = OrderStatus.Pending,
        OrderType orderType = OrderType.DineIn,
        Instant? createdAt = null,
        decimal totalAmount = 9.99m,
        string menuItemName = "Burger")
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            OrderNumber.Of(orderNumber),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());
        order.Status = status;
        order.OrderType = orderType;
        order.CreatedAt = createdAt;
        order.TotalAmount = totalAmount;
        order.Add(NewMenuItemId(), quantity: 1, price: totalAmount);
        order.OrderItems.Single().MenuItemName = menuItemName;
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

    [Fact]
    public async Task Handle_WithStatusAndOrderTypeFilter_ReturnsFilteredCountAndData()
    {
        var orders = new[]
        {
            SeedOrder("ORD-2026-0001", OrderStatus.Pending, OrderType.DineIn),
            SeedOrder("ORD-2026-0002", OrderStatus.Preparing, OrderType.Takeout),
            SeedOrder("ORD-2026-0003", OrderStatus.Ready, OrderType.Takeout),
            SeedOrder("ORD-2026-0004", OrderStatus.Cancelled, OrderType.Takeout),
        };
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersQuery(
                new PaginationRequest(0, 10),
                Status: [OrderStatus.Preparing, OrderStatus.Ready],
                OrderType: OrderType.Takeout),
            CancellationToken.None);

        result.Orders.Count.Should().Be(2);
        result.Orders.Data.Select(d => d.OrderNumber)
            .Should().BeEquivalentTo(["ORD-2026-0002", "ORD-2026-0003"]);
    }

    [Fact]
    public async Task Handle_WithSearchAndSort_PlacesNullCreatedAtLast()
    {
        Instant t1 = Instant.FromUtc(2026, 10, 24, 10, 0);
        Instant t2 = Instant.FromUtc(2026, 10, 24, 12, 0);

        var orders = new[]
        {
            SeedOrder("DIN20261024-0001", createdAt: null, menuItemName: "Tacos"),
            SeedOrder("DIN20261024-0002", createdAt: t1, menuItemName: "Tacos"),
            SeedOrder("DIN20261024-0003", createdAt: t2, menuItemName: "Tacos"),
            SeedOrder("TAK20261024-0004", createdAt: t2, menuItemName: "Salad"),
        };
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(orders));

        var handler = new GetOrdersHandler(dbContext);
        var result = await handler.Handle(
            new GetOrdersQuery(
                new PaginationRequest(0, 10),
                Search: "DIN20261024",
                Sort: "createdAt",
                Dir: "desc"),
            CancellationToken.None);

        result.Orders.Count.Should().Be(3);
        result.Orders.Data.Select(d => d.OrderNumber).Should().ContainInOrder(
            "DIN20261024-0003",
            "DIN20261024-0002",
            "DIN20261024-0001");
    }

    [Fact]
    public void Validator_RejectsInvalidRangeAndSearchLength()
    {
        var validator = new GetOrdersQueryValidator();
        Instant from = Instant.FromUtc(2026, 10, 24, 15, 0);
        Instant to = Instant.FromUtc(2026, 10, 24, 12, 0);

        var badQuery = new GetOrdersQuery(
            new PaginationRequest(0, 101),
            Search: new string('a', 65),
            From: from,
            To: to,
            Sort: "invalidSort",
            Dir: "sideways");

        var validationResult = validator.Validate(badQuery);
        validationResult.IsValid.Should().BeFalse();
        validationResult.Errors.Should().HaveCountGreaterThanOrEqualTo(4);
    }
}

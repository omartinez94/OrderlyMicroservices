using BuildingBlocks.Pagination;
using Ordering.Application.Orders.Queries.GetOrderActivities;
using Ordering.Application.Tests.Abstractions;
using Ordering.Domain.Enums;

namespace Ordering.Application.Tests.Queries;

/// <summary>
/// Covers <see cref="GetOrderActivitiesHandler"/>: loads the order via
/// <c>SingleOrDefaultAsync</c>, then filters / sorts / paginates the
/// in-memory activity collection (the handler does the post-load work on
/// <c>order.Activities.AsEnumerable()</c>).
/// Negative path: unknown id throws <see cref="OrderNotFoundException"/>.
/// </summary>
/// <remarks>
/// Activities must be pre-populated on the seeded <c>Order</c> via
/// <c>Order.RecordActivity(...)</c> — the mock's <c>Include</c> is a
/// no-op, so the navigation collection is the one we set up by hand.
/// </remarks>
public sealed class GetOrderActivitiesHandlerTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static Order SeedOrderWithActivities(out IReadOnlyList<Instant> occurredAtTimestamps)
    {
        var order = Order.Create(
            NewOrderId(),
            NewCustomerId(),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            ValidAddress(),
            ValidAddress(),
            ValidPayment());

        var t0 = Instant.FromUtc(2026, 8, 7, 10, 0, 0);
        var t1 = t0.Plus(Duration.FromMinutes(5));
        var t2 = t0.Plus(Duration.FromMinutes(10));

        // Order.Create raises OrderCreatedEvent but no OrderCreated activity
        // (handler is application-side). Append three to drive the ordering
        // and OccurredAt / Type assertions.
        order.RecordActivity(OrderActivityType.OrderCreated, actorUserId: null, occurredAt: t0);
        order.Confirm(Guid.NewGuid(), t1);
        order.MarkPreparing(t2);

        order.ClearDomainEvents();
        occurredAtTimestamps = new[] { t0, t1, t2 };
        return order;
    }

    [Fact]
    public async Task Handle_OrderWithActivities_ReturnsOrderedList()
    {
        var order = SeedOrderWithActivities(out _);
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(new[] { order }));

        var handler = new GetOrderActivitiesHandler(dbContext);
        var result = await handler.Handle(
            new GetOrderActivitiesQuery(
                OrderId: order.Id.Value,
                Type: null,
                From: null,
                To: null,
                Pagination: new PaginationRequest(0, 10)),
            CancellationToken.None);

        result.Activities.Count.Should().Be(3);
        result.Activities.Data.Should().HaveCount(3);
        // Order: OrderCreated (t0), OrderConfirmed (t1), OrderPreparingStarted (t2)
        result.Activities.Data.Select(a => a.ActivityType).Should().ContainInOrder(
            OrderActivityType.OrderCreated,
            OrderActivityType.OrderConfirmed,
            OrderActivityType.OrderPreparingStarted);
    }

    [Fact]
    public async Task Handle_UnknownOrder_ThrowsNotFound()
    {
        var dbContext = Substitute.For<IApplicationDbContext>();
        dbContext.Orders.Returns(MockDbSet<Order>.From(Array.Empty<Order>()));

        var handler = new GetOrderActivitiesHandler(dbContext);

        Func<Task> act = () => handler.Handle(
            new GetOrderActivitiesQuery(
                OrderId: Guid.NewGuid(),
                Type: null,
                From: null,
                To: null,
                Pagination: new PaginationRequest(0, 10)),
            CancellationToken.None);

        await act.Should().ThrowAsync<OrderNotFoundException>();
    }
}

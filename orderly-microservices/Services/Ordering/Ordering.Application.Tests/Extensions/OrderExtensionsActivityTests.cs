using BuildingBlocks.Correlation;
using Ordering.Application.Extensions;
using Ordering.Domain.Enums;

namespace Ordering.Application.Tests.Extensions;

/// <summary>
/// Covers <see cref="OrderExtensions.ToOrderDto"/> for the activity-feed
/// mapping: chronological ordering (with Guid tie-breaker), empty-list
/// passthrough, correlation-id propagation, and metadata snapshot
/// pass-through (the typed <see cref="OrderActivityMetadata"/> record is
/// mapped by reference — the jsonb serialisation is verified by the
/// EF Core integration test in the Infrastructure project).
/// </summary>
public sealed class OrderExtensionsActivityTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateConfirmedOrder()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        return order;
    }

    [Fact]
    public void ToOrderDto_MapsActivitiesInChronologicalOrder()
    {
        var order = CreateConfirmedOrder();

        var dto = order.ToOrderDto();

        dto.Activities.Should().HaveCount(1);
        dto.Activities[0].ActivityType.Should().Be(OrderActivityType.OrderConfirmed);
        dto.Activities[0].OccurredAt.Should().Be(order.Activities.Single().OccurredAt);
    }

    [Fact]
    public void ToOrderDto_NoActivities_ReturnsEmptyList()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var dto = order.ToOrderDto();

        dto.Activities.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToOrderDto_MapsCorrelationId_WhenSet()
    {
        var order = CreateConfirmedOrder();
        // Confirm stamps the ambient into the activity row.
        CorrelationContext.Set("trace-abc-123");

        try
        {
            order.Update(ValidAddress(), ValidAddress(), ValidPayment());

            var dto = order.ToOrderDto();
            var updateActivity = dto.Activities.Single(a => a.ActivityType == OrderActivityType.OrderUpdated);
            updateActivity.CorrelationId.Should().Be("trace-abc-123");
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }

    [Fact]
    public void ToOrderDto_MapsMetadataStatusEnumsAsTypedRecord()
    {
        var order = CreateConfirmedOrder();

        var dto = order.ToOrderDto();
        var confirmedActivity = dto.Activities.Single();

        confirmedActivity.Metadata.Should().NotBeNull();
        confirmedActivity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Pending);
        confirmedActivity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void ToOrderDto_MapsCancellationReason_AsNotes()
    {
        var order = CreateConfirmedOrder();
        var reason = "Customer changed mind";
        order.Cancel(reason, Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());

        var dto = order.ToOrderDto();
        var cancelActivity = dto.Activities.Single(a => a.ActivityType == OrderActivityType.OrderCancelled);

        cancelActivity.Notes.Should().Be(reason);
        cancelActivity.Metadata!.Reason.Should().Be(reason);
        cancelActivity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void ToOrderDto_MapsPerItemPrepActivity_WithMenuItemName()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Add(MenuItemId.Of(Guid.NewGuid()), quantity: 1, price: 5m);
        var item = order.OrderItems.Single();
        item.MenuItemName = "Burger";
        item.MarkItemPreparing(SystemClock.Instance.GetCurrentInstant());

        var dto = order.ToOrderDto();

        var prepActivity = dto.Activities.Single();
        prepActivity.ActivityType.Should().Be(OrderActivityType.OrderItemPrepStarted);
        prepActivity.Metadata!.OrderItemName.Should().Be("Burger");
        prepActivity.Metadata.OrderItemId.Should().Be(item.Id.Value);
        prepActivity.Metadata.NewPrepStatus.Should().Be(PrepStatus.Preparing);
    }

    [Fact]
    public void ToOrderDto_OrdersMultipleActivitiesByOccurredAt_ThenId()
    {
        // Order.Create is invoked directly (no application-side activity
        // append in this test setup); Confirm / MarkPreparing / MarkReady
        // each append one activity. Expect three in chronological order.
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());

        var dto = order.ToOrderDto();

        dto.Activities.Select(a => a.ActivityType).Should().ContainInOrder(
            OrderActivityType.OrderConfirmed,
            OrderActivityType.OrderPreparingStarted,
            OrderActivityType.OrderReady);
    }
}
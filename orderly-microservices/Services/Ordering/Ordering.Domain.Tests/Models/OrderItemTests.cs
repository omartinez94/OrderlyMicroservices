namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers the per-item prep-state transition methods on
/// <see cref="OrderItem"/>: <c>MarkItemPreparing</c> (Pending -&gt;
/// Preparing) and <c>MarkItemReady</c> (Preparing -&gt; Ready).
/// </summary>
public sealed class OrderItemTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static (Order order, OrderItem item) CreateAttachedItem(PrepStatus initial)
    {
        var order = Order.Create(
            NewOrderId(),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111"));
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);
        var item = order.OrderItems.Single();
        item.MenuItemName = "Test Burger";
        item.PrepStatus = initial;
        item.PrepStartedAt = null;
        item.PrepCompletedAt = null;
        return (order, item);
    }

    private static OrderItem CreateItem(PrepStatus initial) => CreateAttachedItem(initial).item;

    // -------- MarkItemPreparing --------

    [Fact]
    public void MarkItemPreparing_FromPending_TransitionsToPreparing()
    {
        var item = CreateItem(PrepStatus.Pending);
        var now = SystemClock.Instance.GetCurrentInstant();

        item.MarkItemPreparing(now);

        item.PrepStatus.Should().Be(PrepStatus.Preparing);
        item.PrepStartedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(PrepStatus.Preparing)]
    [InlineData(PrepStatus.Ready)]
    public void MarkItemPreparing_FromNonPending_Throws(PrepStatus from)
    {
        var item = CreateItem(from);

        Action act = () => item.MarkItemPreparing(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderItemStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    [Fact]
    public void MarkItemPreparing_AppendsOrderItemPrepStartedActivity_WithMenuItemName_AndPrepStatusMetadata()
    {
        var (order, item) = CreateAttachedItem(PrepStatus.Pending);
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        item.MarkItemPreparing(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderItemPrepStarted);
        activity.OccurredAt.Should().Be(now);
        activity.Metadata!.OrderItemId.Should().Be(item.Id.Value);
        activity.Metadata.OrderItemName.Should().Be("Test Burger");
        activity.Metadata.PreviousPrepStatus.Should().Be(PrepStatus.Pending);
        activity.Metadata.NewPrepStatus.Should().Be(PrepStatus.Preparing);
        activity.Metadata.NewOrderStatus.Should().BeNull();
        activity.Metadata.NewDeliveryStatus.Should().BeNull();
    }

    // -------- MarkItemReady --------

    [Fact]
    public void MarkItemReady_FromPreparing_TransitionsToReady()
    {
        var item = CreateItem(PrepStatus.Preparing);
        var now = SystemClock.Instance.GetCurrentInstant();

        item.MarkItemReady(now);

        item.PrepStatus.Should().Be(PrepStatus.Ready);
        item.PrepCompletedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(PrepStatus.Pending)]
    [InlineData(PrepStatus.Ready)]
    public void MarkItemReady_FromNonPreparing_Throws(PrepStatus from)
    {
        var item = CreateItem(from);

        Action act = () => item.MarkItemReady(SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<InvalidOrderItemStateTransitionException>()
            .Which.FromStatus.Should().Be(from);
    }

    [Fact]
    public void MarkItemReady_AppendsOrderItemPrepCompletedActivity_WithPrepStatusMetadata()
    {
        var (order, item) = CreateAttachedItem(PrepStatus.Preparing);
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        item.MarkItemReady(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderItemPrepCompleted);
        activity.OccurredAt.Should().Be(now);
        activity.Metadata!.OrderItemId.Should().Be(item.Id.Value);
        activity.Metadata.OrderItemName.Should().Be("Test Burger");
        activity.Metadata.PreviousPrepStatus.Should().Be(PrepStatus.Preparing);
        activity.Metadata.NewPrepStatus.Should().Be(PrepStatus.Ready);
    }
}
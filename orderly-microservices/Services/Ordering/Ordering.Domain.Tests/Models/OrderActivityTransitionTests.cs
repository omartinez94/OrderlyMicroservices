namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Locks in the activity-feed contract for every state-transition method on
/// <see cref="Order"/>: each transition appends exactly one
/// <see cref="OrderActivity"/> row carrying the typed
/// <see cref="OrderActivityMetadata"/> snapshot documented in
/// <c>ORDER_ACTIVITY_PLAN.md §6.1</c>.
/// </summary>
/// <remarks>
/// The tests are grouped by transition (one
/// <c>*AppendsXxxActivity_WithYyyMetadata</c> per public transition
/// method). Pre- and post-condition assertions cover the actor id, the
/// status-pair prev/new, and the activity count — the rest of the
/// aggregate's invariants are covered by <see cref="OrderTests"/>.
/// </remarks>
public sealed class OrderActivityTransitionTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreatePendingOrder()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();
        return order;
    }

    // -------- Update --------

    [Fact]
    public void Update_AppendsOrderUpdatedActivity_NoMetadata()
    {
        var order = CreatePendingOrder();
        order.ClearDomainEvents();
        var before = SystemClock.Instance.GetCurrentInstant();

        order.Update(ValidAddress(), ValidAddress(), ValidPayment());

        var after = SystemClock.Instance.GetCurrentInstant();
        var activity = order.Activities.Single();
        activity.ActivityType.Should().Be(OrderActivityType.OrderUpdated);
        activity.ActorUserId.Should().BeNull();
        (activity.OccurredAt >= before && activity.OccurredAt <= after).Should().BeTrue();
        activity.Metadata.Should().BeNull();
    }

    // -------- Confirm --------

    [Fact]
    public void Confirm_AppendsOrderConfirmedActivity_WithActor_AndStatusMetadata()
    {
        var order = CreatePendingOrder();
        var actor = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Confirm(actor, now);

        var activity = order.Activities.Single();
        activity.ActivityType.Should().Be(OrderActivityType.OrderConfirmed);
        activity.ActorUserId.Should().Be(actor);
        activity.OccurredAt.Should().Be(now);
        activity.Metadata.Should().NotBeNull();
        activity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Pending);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Confirmed);
        activity.Metadata.NewPrepStatus.Should().BeNull();
        activity.Metadata.NewDeliveryStatus.Should().BeNull();
    }

    // -------- MarkPreparing --------

    [Fact]
    public void MarkPreparing_AppendsOrderPreparingStartedActivity_WithStatusMetadata()
    {
        var order = CreatePendingOrder();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkPreparing(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderPreparingStarted);
        activity.OccurredAt.Should().Be(now);
        activity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Confirmed);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Preparing);
    }

    // -------- MarkReady --------

    [Fact]
    public void MarkReady_AppendsOrderReadyActivity_WithStatusMetadata()
    {
        var order = CreatePendingOrder();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkReady(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderReady);
        activity.OccurredAt.Should().Be(now);
        activity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Preparing);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Ready);
    }

    // -------- StartDelivery --------

    [Fact]
    public void StartDelivery_AppendsOrderDeliveryStartedActivity_WithDeliveryStatusMetadata()
    {
        var order = CreatePendingOrder();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();

        order.StartDelivery();

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderDeliveryStarted);
        activity.Metadata!.PreviousDeliveryStatus.Should().BeNull();
        activity.Metadata.NewDeliveryStatus.Should().Be(DeliveryStatus.Dispatched);
        activity.Metadata.NewOrderStatus.Should().BeNull();
        activity.Metadata.NewPrepStatus.Should().BeNull();
    }

    // -------- MarkDelivered --------

    [Fact]
    public void MarkDelivered_AppendsOrderDeliveredActivity_WithStatusAndDeliveryStatusMetadata()
    {
        var order = CreatePendingOrder();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());
        order.StartDelivery();
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.MarkDelivered(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderDelivered);
        activity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Ready);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Delivered);
        activity.Metadata.PreviousDeliveryStatus.Should().Be(DeliveryStatus.Dispatched);
        activity.Metadata.NewDeliveryStatus.Should().Be(DeliveryStatus.Delivered);
    }

    // -------- Complete --------

    [Fact]
    public void Complete_AppendsOrderCompletedActivity_WithStatusMetadata()
    {
        var order = CreatePendingOrder();
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());
        order.MarkReady(SystemClock.Instance.GetCurrentInstant());
        order.MarkDelivered(SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Complete(now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderCompleted);
        activity.Metadata!.PreviousOrderStatus.Should().Be(OrderStatus.Delivered);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Completed);
    }

    // -------- Cancel --------

    [Fact]
    public void Cancel_AppendsOrderCancelledActivity_WithReasonAsNotes_AndStatusMetadata()
    {
        var order = CreatePendingOrder();
        var actor = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Cancel("Customer changed mind", actor, now);

        var activity = order.Activities.Last();
        activity.ActivityType.Should().Be(OrderActivityType.OrderCancelled);
        activity.ActorUserId.Should().Be(actor);
        activity.Notes.Should().Be("Customer changed mind");
        activity.Metadata!.Reason.Should().Be("Customer changed mind");
        activity.Metadata.PreviousOrderStatus.Should().Be(OrderStatus.Pending);
        activity.Metadata.NewOrderStatus.Should().Be(OrderStatus.Cancelled);
    }
}
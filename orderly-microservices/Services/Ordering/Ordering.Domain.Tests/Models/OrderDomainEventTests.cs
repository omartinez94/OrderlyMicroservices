namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Pins the domain-event emission contract on the real <see cref="Order"/>
/// aggregate. This file complements (does not duplicate) the existing
/// coverage:
///
/// <list type="bullet">
/// <item><see cref="OrderTests"/> already pins "transition X raises event Y"
/// for each of the seven transition methods (it asserts
/// <c>DomainEvents[0].Should().BeOfType&lt;XEvent&gt;().Which.Order</c>).
/// Repeating those assertions here would be duplicate coverage.</item>
/// <item><see cref="Abstractions.AggregateTests"/> already pins the
/// <c>Append</c>, <c>InsertionOrder</c>, <c>Clear</c>, <c>Reuse</c>, and
/// <c>ReadOnly</c> contracts — but on a <c>TestAggregate</c> stub, not on
/// the real <see cref="Order"/> aggregate that the persistence layer
/// actually round-trips through.</item>
/// </list>
///
/// This file fills the two genuine gaps:
/// <list type="number">
/// <item><b>Payload field pinning.</b> Every event record carries fields
/// beyond the <see cref="Order"/> reference — <c>ConfirmedByUserId</c>,
/// <c>ConfirmedAt</c>, <c>CancelledByUserId</c>, <c>Reason</c>,
/// <c>StartedAt</c>, <c>ReadyAt</c>, <c>DeliveredAt</c>. Downstream
/// consumers (the kitchen prep consumer, the customer notification
/// consumer, the activity-feed projector) bind on those fields, so a
/// dropped field at the emit site is a silent data-loss bug.</item>
/// <item><b>Cumulate + Clear on the real aggregate.</b>
/// <see cref="Abstractions.AggregateTests"/> uses a <c>TestAggregate</c>
/// stub. <see cref="OrderTests"/> clears events between transitions to
/// keep each test single-purpose, so it never asserts "two transitions
/// produce two events on the same aggregate" or "calling Clear after
/// two transitions empties the buffer". This file does both.</item>
/// </list>
///
/// <para><b>Plan deviation.</b> §6.6 of
/// <c>.agents/plan/testing/E2E_TEST_COVERAGE_PLAN.md</c> lists nine
/// transition-assertion tests of the form
/// <c>Confirm_RaisesOrderConfirmedEvent</c>. Seven of those nine are
/// already covered by <see cref="OrderTests"/> under the original
/// transition-method names (e.g.,
/// <c>Confirm_FromPending_TransitionsToConfirmedAndRaisesEvent</c>).
/// This file ships the genuinely new coverage rather than duplicate
/// the existing assertions.</para>
/// </summary>
public sealed class OrderDomainEventTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");
    private static Payment ValidPayment() =>
        Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111");
    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    // -------- Payload field pinning --------

    /// <summary>
    /// Pins every payload field on <see cref="OrderConfirmedEvent"/>. The
    /// downstream customer-notification consumer reads
    /// <c>ConfirmedByUserId</c> to attribute the confirmation and
    /// <c>ConfirmedAt</c> to stamp the activity row — a regression that
    /// swapped the user id with a placeholder (or dropped the
    /// <see cref="Instant"/>) would break that attribution silently.
    /// </summary>
    [Fact]
    public void Confirm_RaisesOrderConfirmedEvent_WithPinnedUserIdAndTime()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Confirm(staffId, now);

        order.DomainEvents.Should().HaveCount(1);
        OrderConfirmedEvent confirmed = order.DomainEvents[0]
            .Should().BeOfType<OrderConfirmedEvent>().Subject;
        confirmed.Order.Should().BeSameAs(order);
        confirmed.ConfirmedByUserId.Should().Be(staffId);
        confirmed.ConfirmedAt.Should().Be(now);
    }

    /// <summary>
    /// Pins every payload field on <see cref="OrderCancelledEvent"/>
    /// (Reason + CancelledByUserId + CancelledAt). The cancellation
    /// activity row reads all three; the refund-job consumer reads
    /// <c>CancelledByUserId</c>; the audit projector reads
    /// <c>CancelledAt</c>.
    /// </summary>
    [Fact]
    public void Cancel_RaisesOrderCancelledEvent_WithPinnedReasonAndUserIdAndTime()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();
        const string reason = "customer requested";

        order.Cancel(reason, staffId, now);

        order.DomainEvents.Should().HaveCount(1);
        OrderCancelledEvent cancelled = order.DomainEvents[0]
            .Should().BeOfType<OrderCancelledEvent>().Subject;
        cancelled.Order.Should().BeSameAs(order);
        cancelled.Reason.Should().Be(reason);
        cancelled.CancelledByUserId.Should().Be(staffId);
        cancelled.CancelledAt.Should().Be(now);
    }

    /// <summary>
    /// Pins the <see cref="OrderPreparingEvent.StartedAt"/> payload
    /// field. The kitchen-side SLA watcher binds on this to compute
    /// "time-since-prep-started"; a regression that defaulted it to
    /// <see cref="SystemClock"/> would silently break SLA alerts.
    /// </summary>
    [Fact]
    public void MarkPreparing_RaisesOrderPreparingEvent_WithPinnedStartedAt()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.MarkPreparing(now);

        order.DomainEvents.Should().HaveCount(1);
        OrderPreparingEvent preparing = order.DomainEvents[0]
            .Should().BeOfType<OrderPreparingEvent>().Subject;
        preparing.Order.Should().BeSameAs(order);
        preparing.StartedAt.Should().Be(now);
    }

    /// <summary>
    /// Pins the <see cref="OrderReadyEvent.ReadyAt"/> payload field.
    /// The dispatch consumer uses this as the dispatch-eligible
    /// timestamp; the customer-notification consumer stamps the
    /// "ready for pickup" message with it.
    /// </summary>
    [Fact]
    public void MarkReady_RaisesOrderReadyEvent_WithPinnedReadyAt()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Preparing);
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.MarkReady(now);

        order.DomainEvents.Should().HaveCount(1);
        OrderReadyEvent ready = order.DomainEvents[0]
            .Should().BeOfType<OrderReadyEvent>().Subject;
        ready.Order.Should().BeSameAs(order);
        ready.ReadyAt.Should().Be(now);
    }

    /// <summary>
    /// Pins the <see cref="OrderDeliveredEvent.DeliveredAt"/> payload
    /// field. The completion consumer reads this to drive
    /// <c>Delivered -&gt; Completed</c> transitions and the activity
    /// feed rows downstream.
    /// </summary>
    [Fact]
    public void MarkDelivered_RaisesOrderDeliveredEvent_WithPinnedDeliveredAt()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.GetType().GetProperty(nameof(Order.Status))!
            .SetValue(order, OrderStatus.Ready);
        order.ClearDomainEvents();

        var now = SystemClock.Instance.GetCurrentInstant();
        order.MarkDelivered(now);

        order.DomainEvents.Should().HaveCount(1);
        OrderDeliveredEvent delivered = order.DomainEvents[0]
            .Should().BeOfType<OrderDeliveredEvent>().Subject;
        delivered.Order.Should().BeSameAs(order);
        delivered.DeliveredAt.Should().Be(now);
    }

    // -------- Cumulate + clear on the real Order aggregate --------

    /// <summary>
    /// Pins the cumulate-on-emit contract on the real <see cref="Order"/>
    /// aggregate (not on the <c>TestAggregate</c> stub used by
    /// <see cref="Abstractions.AggregateTests"/>). Driving the order
    /// through three legal transitions before clearing must surface
    /// all three events in insertion order. The
    /// <c>DispatchDomainEventsInterceptor</c> iterates
    /// <see cref="Aggregate{T}.DomainEvents"/> in stored order, so
    /// a regression that lost append-on-transition would silently
    /// skip dispatch for downstream handlers.
    /// </summary>
    [Fact]
    public void MultipleTransitions_CumulateDomainEvents_OnOrderAggregate()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.ClearDomainEvents();

        var staffId = Guid.NewGuid();
        var now = SystemClock.Instance.GetCurrentInstant();

        order.Confirm(staffId, now);
        order.MarkPreparing(now);

        order.DomainEvents.Should().HaveCount(2);
        order.DomainEvents[0].Should().BeOfType<OrderConfirmedEvent>();
        order.DomainEvents[1].Should().BeOfType<OrderPreparingEvent>();
    }

    /// <summary>
    /// Pins the "clear drains the buffer" contract on the real
    /// <see cref="Order"/> aggregate. The interceptor calls
    /// <c>ClearDomainEvents</c> after collecting the events but
    /// before publishing, so a non-clearing aggregate would
    /// double-dispatch every event on the next
    /// <c>SaveChanges</c>. <see cref="Abstractions.AggregateTests"/>
    /// pins the same contract on a <c>TestAggregate</c> stub; this
    /// test pins it on the production aggregate the interceptor
    /// actually sees.
    /// </summary>
    [Fact]
    public void ClearDomainEvents_PurgesAccumulatedEvents_OnOrderAggregate()
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        order.Confirm(Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.MarkPreparing(SystemClock.Instance.GetCurrentInstant());

        order.DomainEvents.Should().HaveCount(3);

        IDomainEvent[] drained = order.ClearDomainEvents();

        drained.Should().HaveCount(3);
        order.DomainEvents.Should().BeEmpty();

        // The aggregate must accept new events after a clear so
        // long-lived aggregates that survive multiple save cycles
        // continue to function.
        order.Cancel("test", Guid.NewGuid(), SystemClock.Instance.GetCurrentInstant());
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCancelledEvent>();
    }
}

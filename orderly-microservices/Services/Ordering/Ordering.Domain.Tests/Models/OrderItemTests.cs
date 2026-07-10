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

    private static OrderItem CreateItem(PrepStatus initial)
    {
        var order = Order.Create(
            NewOrderId(),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard"));
        order.Add(NewMenuItemId(), quantity: 1, price: 5m);
        var item = order.OrderItems.Single();
        item.PrepStatus = initial;
        item.PrepStartedAt = null;
        item.PrepCompletedAt = null;
        return item;
    }

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
}
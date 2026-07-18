using BuildingBlocks.Correlation;

namespace Ordering.Domain.Tests.Models;

/// <summary>
/// Covers the <see cref="OrderActivity"/> aggregate: factory invariants,
/// correlation-id ambient threading, and the contract that the factory is
/// the only entry point.
/// </summary>
public sealed class OrderActivityTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());

    private static OrderActivity CreateActivity(
        OrderActivityType type = OrderActivityType.OrderConfirmed,
        Guid? actorUserId = null,
        string? correlationId = null,
        string? notes = null,
        OrderActivityMetadata? metadata = null) =>
        OrderActivity.Create(
            NewOrderId(),
            type,
            actorUserId,
            SystemClock.Instance.GetCurrentInstant(),
            correlationId,
            notes,
            metadata);

    [Fact]
    public void Create_StampsCorrelationId_WhenAmbientSet()
    {
        // The factory itself accepts correlationId as a parameter; the
        // ambient is read by Order.RecordActivity and forwarded to the
        // factory. This test exercises that contract.
        var order = Order.Create(
            OrderId.Of(Guid.NewGuid()),
            CustomerId.Of(Guid.NewGuid()),
            OrderNumber.Of("ORD-2026-0001"),
            Guid.NewGuid(),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Address.Of("123 Main St", "Springfield", "IL", "12345", "US"),
            Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111"));

        CorrelationContext.Set("test-corr-id");

        try
        {
            order.Update(
                Address.Of("999 New St", "Springfield", "IL", "12345", "US"),
                Address.Of("999 New St", "Springfield", "IL", "12345", "US"),
                Payment.Of(BuildingBlocks.Messaging.Events.PaymentMethod.Card, "Visa", "1111"));

            order.Activities.Single().CorrelationId.Should().Be("test-corr-id");
        }
        finally
        {
            CorrelationContext.Clear();
        }
    }

    [Fact]
    public void Create_LeavesCorrelationIdNull_WhenAmbientUnset()
    {
        CorrelationContext.Clear();

        var activity = CreateActivity();

        activity.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void Create_WithNullOrderId_Throws()
    {
        Action act = () => OrderActivity.Create(
            null!,
            OrderActivityType.OrderCreated,
            actorUserId: null,
            occurredAt: SystemClock.Instance.GetCurrentInstant());

        act.Should().Throw<ArgumentNullException>().WithParameterName("orderId");
    }

    [Fact]
    public void Create_Throws_WhenNotesExceeds2000Chars()
    {
        var oversized = new string('x', 2001);

        Action act = () => CreateActivity(notes: oversized);

        act.Should().Throw<OrderActivityInvariantException>()
            .WithMessage("*2000*");
    }

    [Fact]
    public void Create_Throws_WhenCorrelationIdExceeds100Chars()
    {
        var oversized = new string('c', 101);

        Action act = () => CreateActivity(correlationId: oversized);

        act.Should().Throw<OrderActivityInvariantException>()
            .WithMessage("*100*");
    }

    [Fact]
    public void Create_Throws_OnUnknownActivityType()
    {
        Action act = () => CreateActivity(type: (OrderActivityType)int.MaxValue);

        act.Should().Throw<OrderActivityInvariantException>()
            .WithMessage("*Unknown activity type*");
    }

    [Theory]
    [InlineData(OrderActivityType.OrderCreated)]
    [InlineData(OrderActivityType.OrderUpdated)]
    [InlineData(OrderActivityType.OrderConfirmed)]
    [InlineData(OrderActivityType.OrderPreparingStarted)]
    [InlineData(OrderActivityType.OrderReady)]
    [InlineData(OrderActivityType.OrderDeliveryStarted)]
    [InlineData(OrderActivityType.OrderDelivered)]
    [InlineData(OrderActivityType.OrderCompleted)]
    [InlineData(OrderActivityType.OrderCancelled)]
    [InlineData(OrderActivityType.OrderItemPrepStarted)]
    [InlineData(OrderActivityType.OrderItemPrepCompleted)]
    public void Create_ForEachEnumValue_BuildsActivity(OrderActivityType type)
    {
        var activity = CreateActivity(type);

        activity.ActivityType.Should().Be(type);
        activity.Id.Value.Should().NotBe(Guid.Empty);
        activity.OrderId.Should().NotBeNull();
    }
}
using Ordering.Application.Extensions;

namespace Ordering.Application.Tests.Extensions;

/// <summary>
/// Acceptance: <c>OrderExtensions.ToOrderCreatedIntegrationEvent</c>
/// surfaces <see cref="KitchenOrderItemVariation"/> and
/// <see cref="KitchenOrderItemCustomization"/> as typed records straight
/// off the aggregate — no string-to-typed-record round trip on the bus.
/// Tests asserted the jsonb-parse path in OrderExtensions;
/// those workarounds are gone and the aggregate is now the single source
/// of truth.
/// </summary>
public sealed class OrderExtensionsPhaseDTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of(PaymentMethod.Card, "Visa", "1111");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateOrderWithItem(
        IReadOnlyList<KitchenOrderItemVariation>? variations = null,
        IReadOnlyList<KitchenOrderItemCustomization>? customizations = null)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        var menuItemId = NewMenuItemId();
        order.Add(menuItemId, quantity: 2, price: 9.99m);
        // Set the typed properties directly — OrderItem has public setters
        // because the BasketCheckoutEventHandler populates them from the
        // DTO before persisting.
        var item = order.OrderItems.Single();
        item.SelectedVariations = variations ?? [];
        item.Customizations = customizations ?? [];
        return order;
    }

    /// <summary>
    /// Acceptance: a realistic Basket payload carries the typed
    /// <see cref="KitchenOrderItemVariation"/> /
    /// <see cref="KitchenOrderItemCustomization"/> records rather than
    /// silently dropping them to empty lists.
    /// </summary>
    [Fact]
    public void ToOrderCreatedIntegrationEvent_RichVariationsAndCustomizations_MapTyped()
    {
        var variations = new List<KitchenOrderItemVariation>
        {
            new("Size: Large", 2.50m),
            new("Extra cheese", 1.00m),
        };
        var customizations = new List<KitchenOrderItemCustomization>
        {
            new("No onions", null, 0m),
            new("Sauce", "Spicy", 0.50m),
        };

        var order = CreateOrderWithItem(variations, customizations);

        var evt = order.ToOrderCreatedIntegrationEvent();

        evt.Items.Should().HaveCount(1);
        var preview = evt.Items[0];

        preview.SelectedVariations.Should().HaveCount(2);
        preview.SelectedVariations[0].Name.Should().Be("Size: Large");
        preview.SelectedVariations[0].Price.Should().Be(2.50m);
        preview.SelectedVariations[1].Name.Should().Be("Extra cheese");
        preview.SelectedVariations[1].Price.Should().Be(1.00m);

        preview.Customizations.Should().HaveCount(2);
        preview.Customizations[0].Label.Should().Be("No onions");
        preview.Customizations[0].Value.Should().BeNull();
        preview.Customizations[0].Price.Should().Be(0m);
        preview.Customizations[1].Label.Should().Be("Sauce");
        preview.Customizations[1].Value.Should().Be("Spicy");
        preview.Customizations[1].Price.Should().Be(0.50m);
    }

    /// <summary>
    /// Acceptance: an item with no variations / customizations still
    /// serializes with empty lists — never null. Downstream code can
    /// iterate unconditionally.
    /// </summary>
    [Fact]
    public void ToOrderCreatedIntegrationEvent_NoVariationsOrCustomizations_EmitsEmptyLists()
    {
        var order = CreateOrderWithItem();

        var evt = order.ToOrderCreatedIntegrationEvent();

        var preview = evt.Items[0];
        preview.SelectedVariations.Should().NotBeNull().And.BeEmpty();
        preview.Customizations.Should().NotBeNull().And.BeEmpty();
    }
}
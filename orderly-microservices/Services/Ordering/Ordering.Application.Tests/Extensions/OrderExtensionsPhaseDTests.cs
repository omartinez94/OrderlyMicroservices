using Ordering.Application.Extensions;

namespace Ordering.Application.Tests.Extensions;

/// <summary>
/// <c>OrderExtensions.ToOrderCreatedIntegrationEvent</c>
/// must surface <see cref="KitchenOrderItemVariation"/> and
/// <see cref="KitchenOrderItemCustomization"/> as typed records (not raw
/// strings), even when the source jsonb is richer than the legacy
/// <c>string[]</c> shape that the aggregate still stores.
/// </summary>
public sealed class OrderExtensionsPhaseDTests
{
    private static OrderId NewOrderId() => OrderId.Of(Guid.NewGuid());
    private static CustomerId NewCustomerId() => CustomerId.Of(Guid.NewGuid());
    private static MenuItemId NewMenuItemId() => MenuItemId.Of(Guid.NewGuid());

    private static Address ValidAddress() =>
        Address.Of("123 Main St", "Springfield", "IL", "12345", "US");

    private static Payment ValidPayment() =>
        Payment.Of("John Doe", "4111111111111111", "12/30", "123", "CreditCard");

    private static OrderNumber ValidOrderNumber() => OrderNumber.Of("ORD-2026-0001");

    private static Order CreateOrderWithItem(string selectedVariationsJson, string customizationsJson)
    {
        var order = Order.Create(
            NewOrderId(), NewCustomerId(), ValidOrderNumber(),
            Guid.NewGuid(), ValidAddress(), ValidAddress(), ValidPayment());
        var menuItemId = NewMenuItemId();
        order.Add(menuItemId, quantity: 2, price: 9.99m);
        // Seed the jsonb columns directly. OrderItem has internal setters
        // for these fields (no aggregate method) — they're populated by
        // the BasketCheckoutEventHandler in production.
        var item = order.OrderItems.Single();
        item.SelectedVariations = selectedVariationsJson;
        item.Customizations = customizationsJson;
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
        // Realistic Basket payload shape — each variation carries
        // { Name, Price }; each customization carries { Label, Value, Price }.
        const string variationsJson =
            "[{\"Name\":\"Size: Large\",\"Price\":2.50}," +
            "{\"Name\":\"Extra cheese\",\"Price\":1.00}]";
        const string customizationsJson =
            "[{\"Label\":\"No onions\",\"Value\":null,\"Price\":0}," +
            "{\"Label\":\"Sauce\",\"Value\":\"Spicy\",\"Price\":0.50}]";

        var order = CreateOrderWithItem(variationsJson, customizationsJson);

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
    /// Backwards-compat: the legacy <c>string[]</c> jsonb shape still
    /// produces one typed record per entry, with <c>Price</c> = 0. No
    /// data is silently dropped.
    /// </summary>
    [Fact]
    public void ToOrderCreatedIntegrationEvent_LegacyStringArray_MapsToTypedRecords()
    {
        const string variationsJson = "[\"Size: Large\", \"Extra cheese\"]";
        const string customizationsJson = "[\"No onions\", \"Sauce: Spicy\"]";

        var order = CreateOrderWithItem(variationsJson, customizationsJson);

        var evt = order.ToOrderCreatedIntegrationEvent();

        var preview = evt.Items[0];
        preview.SelectedVariations.Should().HaveCount(2);
        preview.SelectedVariations[0].Name.Should().Be("Size: Large");
        preview.SelectedVariations[0].Price.Should().Be(0m);
        preview.SelectedVariations[1].Name.Should().Be("Extra cheese");

        preview.Customizations.Should().HaveCount(2);
        preview.Customizations[0].Label.Should().Be("No onions");
        preview.Customizations[0].Value.Should().BeNull();
        preview.Customizations[1].Label.Should().Be("Sauce: Spicy");
    }

    /// <summary>
    /// Acceptance: an item with no variations / customizations still
    /// serializes with empty lists — never null. Downstream code can
    /// iterate unconditionally.
    /// </summary>
    [Fact]
    public void ToOrderCreatedIntegrationEvent_NoVariationsOrCustomizations_EmitsEmptyLists()
    {
        var order = CreateOrderWithItem(string.Empty, string.Empty);

        var evt = order.ToOrderCreatedIntegrationEvent();

        var preview = evt.Items[0];
        preview.SelectedVariations.Should().NotBeNull().And.BeEmpty();
        preview.Customizations.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// Malformed jsonb must NOT propagate an exception onto the bus.
    /// The integration event is still emitted with empty lists so the
    /// kitchen display degrades gracefully.
    /// </summary>
    [Fact]
    public void ToOrderCreatedIntegrationEvent_MalformedJson_FallsBackToEmptyLists()
    {
        var order = CreateOrderWithItem("not valid json", "{not: [valid");

        var evt = order.ToOrderCreatedIntegrationEvent();

        var preview = evt.Items[0];
        preview.SelectedVariations.Should().BeEmpty();
        preview.Customizations.Should().BeEmpty();
    }
}
namespace Ordering.Application.Tests.Dtos.Validators;

using BuildingBlocks.Messaging.Events;
using FluentValidation.TestHelper;
using Ordering.Application.Dtos;
using Ordering.Application.Dtos.Validators;

public class OrderDtoValidatorTests
{
    private readonly OrderDtoValidator _validator = new();

    private static OrderItemDto CreateValidItem() => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: Guid.NewGuid(),
        MenuItemName: "Burger",
        MenuItemDescription: "Tasty",
        MenuItemImageUrl: "https://example.test/img.png",
        Quantity: 1,
        UnitPrice: 10m,
        BasePrice: 10m,
        TotalPrice: 10m,
        SeatNumber: 1,
        SpecialInstructions: string.Empty,
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null,
        CourseNumber: 1);

    private static OrderDto CreateValidOrder(
        OrderType orderType = OrderType.DineIn,
        Guid? customerId = null,
        AddressDto? billingAddress = null,
        AddressDto? deliveryAddress = null,
        PaymentDto? payment = null,
        decimal subtotal = 10m,
        decimal total = 10m) => new(
        Id: Guid.NewGuid(),
        CustomerId: customerId ?? (orderType == OrderType.Delivery ? Guid.NewGuid() : Guid.Empty),
        OrderNumber: "ORD-1",
        RestaurantId: Guid.NewGuid(),
        Currency: "USD",
        Subtotal: subtotal,
        TaxRate: 0.1m,
        TaxAmount: 1m,
        DiscountAmount: 0m,
        DiscountCode: string.Empty,
        TotalAmount: total,
        Status: OrderStatus.Pending,
        OrderType: orderType,
        BillingAddress: billingAddress,
        DeliveryAddress: deliveryAddress,
        DeliveryNotes: string.Empty,
        DeliveryStatus: null,
        DeliveryLatitude: null,
        DeliveryLongitude: null,
        Payment: payment,
        EstimatedPrepTimeMinutes: 15,
        ActualPrepTimeMinutes: 0,
        IsModified: false,
        RequiresAdminApproval: false,
        TableId: null,
        CreatedByUserId: Guid.NewGuid(),
        ApprovedByAdminId: null,
        ConfirmedByUserId: null,
        CompletedByUserId: null,
        ApprovedAt: null,
        CancelledAt: null,
        CompletedAt: null,
        ConfirmedAt: null,
        DeliveredAt: null,
        PreparingStartedAt: null,
        ReadyAt: null,
        Notes: string.Empty,
        OrderItems: [CreateValidItem()],
        Activities: []);

    [Fact]
    public void Validate_PosDineInWithEmptyCustomerAndNoAddressesOrPayment_IsValid()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.DineIn,
            customerId: Guid.Empty,
            billingAddress: null,
            deliveryAddress: null,
            payment: null,
            subtotal: 0m,
            total: 0m);

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_PosTakeoutWithEmptyCustomerAndNoAddressesOrPayment_IsValid()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.Takeout,
            customerId: Guid.Empty,
            billingAddress: null,
            deliveryAddress: null,
            payment: null);

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DeliveryWithEmptyCustomerId_FailsValidation()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.Delivery,
            customerId: Guid.Empty,
            billingAddress: new AddressDto("123 Main", "City", "ST", "12345", "USA"),
            deliveryAddress: new AddressDto("123 Main", "City", "ST", "12345", "USA"),
            payment: new PaymentDto(PaymentMethod.Card, "Visa", "1234"));

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldHaveValidationErrorFor(x => x.CustomerId)
            .WithErrorMessage("CustomerId is required for delivery orders.");
    }

    [Fact]
    public void Validate_DeliveryWithoutAddressesOrPayment_FailsValidation()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.Delivery,
            customerId: Guid.NewGuid(),
            billingAddress: null,
            deliveryAddress: null,
            payment: null);

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldHaveValidationErrorFor(x => x.BillingAddress)
            .WithErrorMessage("BillingAddress is required for delivery orders.");
        result.ShouldHaveValidationErrorFor(x => x.DeliveryAddress)
            .WithErrorMessage("DeliveryAddress is required for delivery orders.");
        result.ShouldHaveValidationErrorFor(x => x.Payment)
            .WithErrorMessage("Payment is required for delivery orders.");
    }

    [Fact]
    public void Validate_DeliveryWithAllFields_IsValid()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.Delivery,
            customerId: Guid.NewGuid(),
            billingAddress: new AddressDto("123 Main", "City", "ST", "12345", "USA"),
            deliveryAddress: new AddressDto("123 Main", "City", "ST", "12345", "USA"),
            payment: new PaymentDto(PaymentMethod.Card, "Visa", "1234"));

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenPaymentSupplied_ValidatesPaymentDto()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.DineIn,
            payment: new PaymentDto(PaymentMethod.Unspecified, string.Empty, "bad"));

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldHaveValidationErrorFor("Payment.Brand");
        result.ShouldHaveValidationErrorFor("Payment.LastFour");
    }

    [Fact]
    public void Validate_WhenSubtotalOrTotalNegative_FailsValidation()
    {
        OrderDto order = CreateValidOrder(
            orderType: OrderType.DineIn,
            subtotal: -1m,
            total: -5m);

        TestValidationResult<OrderDto> result = _validator.TestValidate(order);

        result.ShouldHaveValidationErrorFor(x => x.Subtotal);
        result.ShouldHaveValidationErrorFor(x => x.TotalAmount);
    }
}

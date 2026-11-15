namespace Ordering.Application.Tests.Dtos.Validators;

using BuildingBlocks.Messaging.Events;
using FluentValidation.TestHelper;
using Ordering.Application.Dtos;
using Ordering.Application.Dtos.Validators;

public class OrderItemDtoValidatorTests
{
    private readonly OrderItemDtoValidator _validator = new();

    private static OrderItemDto CreateItem(
        decimal unitPrice = 10m,
        decimal totalPrice = 10m,
        int seatNumber = 1) => new(
        Id: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        MenuItemId: Guid.NewGuid(),
        MenuItemName: "Burger",
        MenuItemDescription: "Tasty",
        MenuItemImageUrl: "https://example.test/img.png",
        Quantity: 1,
        UnitPrice: unitPrice,
        BasePrice: unitPrice,
        TotalPrice: totalPrice,
        SeatNumber: seatNumber,
        SpecialInstructions: string.Empty,
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null);

    [Fact]
    public void Validate_ZeroUnitPriceAndTotalPrice_IsValid()
    {
        OrderItemDto item = CreateItem(unitPrice: 0m, totalPrice: 0m);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldNotHaveValidationErrorFor(x => x.UnitPrice);
        result.ShouldNotHaveValidationErrorFor(x => x.TotalPrice);
    }

    [Fact]
    public void Validate_NegativeUnitPriceOrTotalPrice_FailsValidation()
    {
        OrderItemDto item = CreateItem(unitPrice: -1m, totalPrice: -1m);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice);
        result.ShouldHaveValidationErrorFor(x => x.TotalPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public void Validate_NonNegativeSeatNumber_IsValid(int seatNumber)
    {
        OrderItemDto item = CreateItem(seatNumber: seatNumber);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldNotHaveValidationErrorFor(x => x.SeatNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Validate_NegativeSeatNumber_FailsValidation(int seatNumber)
    {
        OrderItemDto item = CreateItem(seatNumber: seatNumber);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldHaveValidationErrorFor(x => x.SeatNumber)
            .WithErrorMessage("SeatNumber must be non-negative.");
    }
}

namespace Ordering.Application.Tests.Dtos.Validators;

using BuildingBlocks.Messaging.Events;
using FluentValidation.TestHelper;
using Ordering.Application.Dtos;
using Ordering.Application.Dtos.Validators;

public class OrderItemDtoValidatorTests
{
    private readonly OrderItemDtoValidator _validator = new();

    private static OrderItemDto CreateItem(
        int? courseNumber = null,
        decimal unitPrice = 10m,
        decimal totalPrice = 10m) => new(
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
        SeatNumber: 1,
        SpecialInstructions: string.Empty,
        Customizations: [],
        SelectedVariations: [],
        PrepStatus: PrepStatus.Pending,
        CreatedAt: SystemClock.Instance.GetCurrentInstant(),
        PrepStartedAt: null,
        PrepCompletedAt: null,
        CourseNumber: courseNumber);

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Validate_ValidCourseNumber_IsValid(int? courseNumber)
    {
        OrderItemDto item = CreateItem(courseNumber: courseNumber);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldNotHaveValidationErrorFor(x => x.CourseNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-1)]
    public void Validate_InvalidCourseNumber_FailsValidation(int courseNumber)
    {
        OrderItemDto item = CreateItem(courseNumber: courseNumber);

        TestValidationResult<OrderItemDto> result = _validator.TestValidate(item);

        result.ShouldHaveValidationErrorFor(x => x.CourseNumber)
            .WithErrorMessage("CourseNumber must be between 1 and 9.");
    }

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
}

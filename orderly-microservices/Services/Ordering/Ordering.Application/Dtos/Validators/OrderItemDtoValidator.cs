namespace Ordering.Application.Dtos.Validators;

public class OrderItemDtoValidator : AbstractValidator<OrderItemDto>
{
    public OrderItemDtoValidator()
    {
        RuleFor(x => x.MenuItemId)
            .NotEmpty().WithMessage("MenuItemId is required.");

        RuleFor(x => x.MenuItemName)
            .NotEmpty().WithMessage("MenuItemName is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("UnitPrice must be non-negative.");

        RuleFor(x => x.TotalPrice)
            .GreaterThanOrEqualTo(0).WithMessage("TotalPrice must be non-negative.");

        RuleFor(x => x.SeatNumber)
            .GreaterThanOrEqualTo(0).WithMessage("SeatNumber must be non-negative.");

        RuleFor(x => x.PrepStatus)
            .IsInEnum().WithMessage("PrepStatus is not a valid PrepStatus value.");

        RuleFor(x => x.CourseNumber)
            .InclusiveBetween(1, 9)
            .When(x => x.CourseNumber.HasValue)
            .WithMessage("CourseNumber must be between 1 and 9.");
    }
}

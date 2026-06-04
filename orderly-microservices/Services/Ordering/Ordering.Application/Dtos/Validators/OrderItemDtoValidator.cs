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
            .GreaterThan(0).WithMessage("UnitPrice must be greater than zero.");

        RuleFor(x => x.TotalPrice)
            .GreaterThan(0).WithMessage("TotalPrice must be greater than zero.");

        RuleFor(x => x.PrepStatus)
            .IsInEnum().WithMessage("PrepStatus is not a valid PrepStatus value.");
    }
}

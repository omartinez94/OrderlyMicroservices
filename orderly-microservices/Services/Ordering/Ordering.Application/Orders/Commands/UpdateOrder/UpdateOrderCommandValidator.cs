namespace Ordering.Application.Orders.Commands.UpdateOrder;

public class UpdateOrderCommandValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderCommandValidator()
    {
        RuleFor(x => x.Order.Id)
            .NotEmpty().WithMessage("Order Id is required.");

        RuleFor(x => x.Order)
            .NotNull().WithMessage("Order is required.")
            .SetValidator(new OrderDtoValidator());
    }
}

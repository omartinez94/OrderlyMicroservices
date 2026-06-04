namespace Ordering.Application.Orders.Commands.CreateOrder;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Order)
            .NotNull().WithMessage("Order is required.")
            .SetValidator(new OrderDtoValidator());
    }
}

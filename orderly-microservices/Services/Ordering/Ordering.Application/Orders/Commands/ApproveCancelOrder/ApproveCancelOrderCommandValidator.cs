namespace Ordering.Application.Orders.Commands.ApproveCancelOrder;

public class ApproveCancelOrderCommandValidator : AbstractValidator<ApproveCancelOrderCommand>
{
    public ApproveCancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order Id is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}

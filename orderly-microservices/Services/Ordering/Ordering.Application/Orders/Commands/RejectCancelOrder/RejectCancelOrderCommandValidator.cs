namespace Ordering.Application.Orders.Commands.RejectCancelOrder;

public class RejectCancelOrderCommandValidator : AbstractValidator<RejectCancelOrderCommand>
{
    public RejectCancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order Id is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}

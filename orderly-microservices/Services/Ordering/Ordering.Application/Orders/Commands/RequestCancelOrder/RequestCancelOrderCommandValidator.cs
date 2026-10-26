namespace Ordering.Application.Orders.Commands.RequestCancelOrder;

public class RequestCancelOrderCommandValidator : AbstractValidator<RequestCancelOrderCommand>
{
    public RequestCancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order Id is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}

namespace Ordering.Application.Dtos.Validators;

public class OrderDtoValidator : AbstractValidator<OrderDto>
{
    public OrderDtoValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("CustomerId is required.");

        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required.");

        RuleFor(x => x.OrderNumber)
            .NotEmpty().WithMessage("OrderNumber is required.")
            .MaximumLength(50).WithMessage("OrderNumber must not exceed 50 characters.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a valid 3-letter ISO 4217 code.");

        RuleFor(x => x.Subtotal)
            .GreaterThan(0).WithMessage("Subtotal must be greater than zero.");

        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0, 1).WithMessage("TaxRate must be between 0 and 1.");

        RuleFor(x => x.TaxAmount)
            .GreaterThanOrEqualTo(0).WithMessage("TaxAmount must be non-negative.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage("DiscountAmount must be non-negative.");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("TotalAmount must be greater than zero.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Status is not a valid OrderStatus value.");

        RuleFor(x => x.OrderType)
            .IsInEnum().WithMessage("OrderType is not a valid OrderType value.");

        RuleFor(x => x.BillingAddress)
            .NotNull().WithMessage("BillingAddress is required.")
            .SetValidator(new AddressDtoValidator());

        RuleFor(x => x.DeliveryAddress)
            .NotNull().WithMessage("DeliveryAddress is required.")
            .SetValidator(new AddressDtoValidator());

        RuleFor(x => x.Payment)
            .NotNull().WithMessage("Payment is required.")
            .SetValidator(new PaymentDtoValidator());

        RuleFor(x => x.EstimatedPrepTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("EstimatedPrepTimeMinutes must be non-negative.");

        RuleFor(x => x.OrderItems)
            .NotNull().WithMessage("OrderItems are required.")
            .NotEmpty().WithMessage("At least one OrderItem is required.");

        RuleForEach(x => x.OrderItems)
            .SetValidator(new OrderItemDtoValidator());
    }
}

namespace Ordering.Application.Dtos.Validators;

public class OrderDtoValidator : AbstractValidator<OrderDto>
{
    public OrderDtoValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .When(x => x.OrderType == OrderType.Delivery)
            .WithMessage("CustomerId is required for delivery orders.");

        RuleFor(x => x.RestaurantId)
            .NotEmpty().WithMessage("RestaurantId is required.");

        RuleFor(x => x.OrderNumber)
            .MaximumLength(50).WithMessage("OrderNumber must not exceed 50 characters.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a valid 3-letter ISO 4217 code.");

        RuleFor(x => x.Subtotal)
            .GreaterThanOrEqualTo(0).WithMessage("Subtotal must be non-negative.");

        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0, 1).WithMessage("TaxRate must be between 0 and 1.");

        RuleFor(x => x.TaxAmount)
            .GreaterThanOrEqualTo(0).WithMessage("TaxAmount must be non-negative.");

        RuleFor(x => x.DiscountAmount)
            .GreaterThanOrEqualTo(0).WithMessage("DiscountAmount must be non-negative.");

        RuleFor(x => x.TotalAmount)
            .GreaterThanOrEqualTo(0).WithMessage("TotalAmount must be non-negative.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Status is not a valid OrderStatus value.");

        RuleFor(x => x.OrderType)
            .IsInEnum().WithMessage("OrderType is not a valid OrderType value.");

        RuleFor(x => x.BillingAddress)
            .NotNull()
            .When(x => x.OrderType == OrderType.Delivery)
            .WithMessage("BillingAddress is required for delivery orders.");

        RuleFor(x => x.BillingAddress!)
            .SetValidator(new AddressDtoValidator())
            .When(x => x.BillingAddress is not null);

        RuleFor(x => x.DeliveryAddress)
            .NotNull()
            .When(x => x.OrderType == OrderType.Delivery)
            .WithMessage("DeliveryAddress is required for delivery orders.");

        RuleFor(x => x.DeliveryAddress!)
            .SetValidator(new AddressDtoValidator())
            .When(x => x.DeliveryAddress is not null);

        RuleFor(x => x.Payment)
            .NotNull()
            .When(x => x.OrderType == OrderType.Delivery)
            .WithMessage("Payment is required for delivery orders.");

        RuleFor(x => x.Payment!)
            .SetValidator(new PaymentDtoValidator())
            .When(x => x.Payment is not null);

        RuleFor(x => x.EstimatedPrepTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("EstimatedPrepTimeMinutes must be non-negative.");

        RuleFor(x => x.OrderItems)
            .NotNull().WithMessage("OrderItems are required.")
            .NotEmpty().WithMessage("At least one OrderItem is required.");

        RuleForEach(x => x.OrderItems)
            .SetValidator(new OrderItemDtoValidator());
    }
}

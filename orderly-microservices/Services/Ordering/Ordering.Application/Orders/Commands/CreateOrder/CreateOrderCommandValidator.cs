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

public class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Street is required.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.");

        RuleFor(x => x.State)
            .NotEmpty().WithMessage("State is required.");

        RuleFor(x => x.ZipCode)
            .NotEmpty().WithMessage("ZipCode is required.");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required.");
    }
}

public class PaymentDtoValidator : AbstractValidator<PaymentDto>
{
    public PaymentDtoValidator()
    {
        RuleFor(x => x.CardName)
            .NotEmpty().WithMessage("CardName is required.");

        RuleFor(x => x.CardNumber)
            .NotEmpty().WithMessage("CardNumber is required.")
            .CreditCard().WithMessage("CardNumber is not a valid credit card number.");

        RuleFor(x => x.Expiration)
            .NotEmpty().WithMessage("Expiration is required.")
            .Matches(@"^(0[1-9]|1[0-2])\/\d{2}$").WithMessage("Expiration must be in MM/YY format.");

        RuleFor(x => x.Ccv)
            .NotEmpty().WithMessage("CCV is required.")
            .Matches(@"^\d{3,4}$").WithMessage("CCV must be 3 or 4 digits.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("PaymentMethod is required.");
    }
}

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

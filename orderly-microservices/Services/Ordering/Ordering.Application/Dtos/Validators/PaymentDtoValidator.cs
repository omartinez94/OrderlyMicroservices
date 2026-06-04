namespace Ordering.Application.Dtos.Validators;

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

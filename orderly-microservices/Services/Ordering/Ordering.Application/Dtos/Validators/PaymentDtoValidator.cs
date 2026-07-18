using BuildingBlocks.Messaging.Events;

namespace Ordering.Application.Dtos.Validators;

/// <summary>
/// Validates the redacted <see cref="PaymentDto"/> shape that the
/// ordering pipeline carries after the plan §0.4.10 wire-shape
/// redaction. Rejects <see cref="PaymentMethod.Unspecified"/> (the
/// sentinel exists only for legacy rows) and empty Brand / LastFour.
/// </summary>
public class PaymentDtoValidator : AbstractValidator<PaymentDto>
{
    public PaymentDtoValidator()
    {
        RuleFor(x => x.Method)
            .IsInEnum().WithMessage("PaymentMethod must be a defined enum value.")
            .NotEqual(PaymentMethod.Unspecified).WithMessage("PaymentMethod.Unspecified is reserved for legacy rows; fresh orders must carry a defined method (Card / Cash / Wallet).");

        RuleFor(x => x.Brand)
            .NotEmpty().WithMessage("Brand is required.");

        RuleFor(x => x.LastFour)
            .NotEmpty().WithMessage("LastFour is required.")
            .Matches(@"^\d{4}$").WithMessage("LastFour must be exactly 4 digits.");
    }
}

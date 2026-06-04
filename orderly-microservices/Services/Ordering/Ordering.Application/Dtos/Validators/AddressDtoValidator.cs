namespace Ordering.Application.Dtos.Validators;

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

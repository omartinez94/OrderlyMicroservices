namespace Catalog.API.Features.MenuItems.PatchMenuItemAvailability;

public sealed class PatchMenuItemAvailabilityValidator : AbstractValidator<PatchMenuItemAvailabilityCommand>
{
    public PatchMenuItemAvailabilityValidator()
    {
        RuleFor(x => x.RestaurantId)
            .NotEmpty()
            .WithMessage("RestaurantId is required");

        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required");

        RuleFor(x => x.AvailabilityStatus)
            .NotEmpty()
            .WithMessage("AvailabilityStatus is required")
            .Must(BeValidAvailabilityStatus)
            .WithMessage("AvailabilityStatus must be one of: Available, Limited, Unavailable");
    }

    private static bool BeValidAvailabilityStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return Enum.TryParse(status, ignoreCase: true, out AvailabilityStatus parsed) &&
               Enum.IsDefined(parsed) &&
               !int.TryParse(status, out _);
    }
}

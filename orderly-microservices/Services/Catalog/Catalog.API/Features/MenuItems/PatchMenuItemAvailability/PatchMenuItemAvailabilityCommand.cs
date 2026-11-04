namespace Catalog.API.Features.MenuItems.PatchMenuItemAvailability;

public sealed record PatchMenuItemAvailabilityCommand(
    Guid RestaurantId,
    Guid Id,
    string AvailabilityStatus) : ICommand<PatchMenuItemAvailabilityResult>;

public sealed record PatchMenuItemAvailabilityResult(
    bool Success,
    bool IsAvailable,
    string AvailabilityStatus);

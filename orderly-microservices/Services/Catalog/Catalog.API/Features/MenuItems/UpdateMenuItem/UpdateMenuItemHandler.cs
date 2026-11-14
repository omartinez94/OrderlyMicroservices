using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.MenuItems.UpdateMenuItem;

public class UpdateMenuItemCommand : ICommand<UpdateMenuItemResult>
{
    public Guid Id { get; set; }
    public int? SubCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string? ImageUrl { get; set; }
    public int PrepTimeMinutes { get; set; }
    public int PrepTimeMaxMinutes { get; set; }
    public ItemType ItemType { get; set; }
    public bool IsAvailable { get; set; }
    public AvailabilityStatus AvailabilityStatus { get; set; }
    public LocalDate? SeasonStartDate { get; set; }
    public LocalDate? SeasonEndDate { get; set; }
    public decimal? PromoPrice { get; set; }
    public Instant? PromoStartDate { get; set; }
    public Instant? PromoEndDate { get; set; }
    public int DisplayOrder { get; set; }
    public int? DefaultCourseNumber { get; set; }
}

public record UpdateMenuItemResult(bool Success);

public class UpdateMenuItemCommandValidator : AbstractValidator<UpdateMenuItemCommand>
{
    public UpdateMenuItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).WithMessage("BasePrice must be greater than or equal to 0");
        RuleFor(x => x.ImageUrl)
            .MaximumLength(500).WithMessage("ImageUrl must not exceed 500 characters")
            .Must(url => string.IsNullOrEmpty(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be a valid absolute URL when provided");
        RuleFor(x => x.DefaultCourseNumber)
            .InclusiveBetween(1, 9)
            .When(x => x.DefaultCourseNumber.HasValue)
            .WithMessage("DefaultCourseNumber must be between 1 and 9");
    }
}

internal class UpdateMenuItemCommandHandler(
    CatalogDbContext dbContext,
    IMenuItemMutationNotifier notifier,
    IPriceHistoryRecorder priceHistory,
    ICurrentUser currentUser) : ICommandHandler<UpdateMenuItemCommand, UpdateMenuItemResult>
{
    public async Task<UpdateMenuItemResult> Handle(UpdateMenuItemCommand command, CancellationToken cancellationToken)
    {
        MenuItem? menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), command.Id);
        }

        currentUser.EnsureTenantAccess(menuItem.RestaurantId);

        // Capture the old base price BEFORE the mutation so the audit row
        // can be recorded against the current persisted value.
        decimal oldBasePrice = menuItem.BasePrice;

        menuItem.SubCategoryId = command.SubCategoryId;
        menuItem.Name = command.Name;
        menuItem.Description = command.Description ?? string.Empty;
        menuItem.BasePrice = command.BasePrice;
        // ImageUrl is intentionally ignored/immutable on PUT to prevent stale edit forms from overwriting uploaded images.
        menuItem.PrepTimeMinutes = command.PrepTimeMinutes;
        menuItem.PrepTimeMaxMinutes = command.PrepTimeMaxMinutes;
        menuItem.ItemType = command.ItemType;
        menuItem.AvailabilityStatus = command.AvailabilityStatus;
        menuItem.IsAvailable = command.AvailabilityStatus != AvailabilityStatus.Unavailable;
        menuItem.SeasonStartDate = command.SeasonStartDate;
        menuItem.SeasonEndDate = command.SeasonEndDate;
        menuItem.PromoPrice = command.PromoPrice;
        menuItem.PromoStartDate = command.PromoStartDate;
        menuItem.PromoEndDate = command.PromoEndDate;
        menuItem.DisplayOrder = command.DisplayOrder;
        menuItem.DefaultCourseNumber = command.DefaultCourseNumber;

        // Phase 4: append a PriceHistory row when the base price changed.
        // The recorder skips no-op writes internally.
        priceHistory.Record(
            restaurantId: menuItem.RestaurantId,
            priceType: PriceType.BasePrice,
            oldPrice: oldBasePrice,
            newPrice: command.BasePrice,
            reason: $"MenuItem {menuItem.Id} base price update",
            changedByUserId: currentUser.UserId,
            menuItemId: menuItem.Id,
            ct: cancellationToken);

        await notifier.SaveAndNotifyAsync(menuItem, MenuItemChangeType.Updated, cancellationToken).ConfigureAwait(false);

        return new UpdateMenuItemResult(true);
    }
}

namespace Catalog.API.Features.MenuItems.CreateMenuItem;

public class CreateMenuItemCommand : ICommand<CreateMenuItemResult>
{
    public Guid RestaurantId { get; set; }
    public int? SubCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
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
}

public record CreateMenuItemResult(Guid Id);

public class CreateMenuItemCommandValidator : AbstractValidator<CreateMenuItemCommand>
{
    public CreateMenuItemCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).WithMessage("BasePrice must be greater than or equal to 0");
    }
}

internal class CreateMenuItemCommandHandler(
    CatalogDbContext dbContext,
    ICatalogCache cache) : ICommandHandler<CreateMenuItemCommand, CreateMenuItemResult>
{
    public async Task<CreateMenuItemResult> Handle(CreateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var menuItem = new MenuItem
        {
            Id = Guid.NewGuid(),
            RestaurantId = command.RestaurantId,
            SubCategoryId = command.SubCategoryId,
            Name = command.Name,
            Description = command.Description,
            BasePrice = command.BasePrice,
            ImageUrl = command.ImageUrl,
            PrepTimeMinutes = command.PrepTimeMinutes,
            PrepTimeMaxMinutes = command.PrepTimeMaxMinutes,
            ItemType = command.ItemType,
            IsAvailable = command.IsAvailable,
            AvailabilityStatus = command.AvailabilityStatus,
            SeasonStartDate = command.SeasonStartDate,
            SeasonEndDate = command.SeasonEndDate,
            PromoPrice = command.PromoPrice,
            PromoStartDate = command.PromoStartDate,
            PromoEndDate = command.PromoEndDate,
            DisplayOrder = command.DisplayOrder,
            IsDeleted = false
        };

        dbContext.MenuItems.Add(menuItem);
        await dbContext.SaveChangesAsync(cancellationToken);

        await cache.InvalidateMenuAsync(command.RestaurantId, cancellationToken);

        return new CreateMenuItemResult(menuItem.Id);
    }
}

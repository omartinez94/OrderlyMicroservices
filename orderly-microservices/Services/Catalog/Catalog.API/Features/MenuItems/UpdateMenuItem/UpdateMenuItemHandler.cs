namespace Catalog.API.Features.MenuItems.UpdateMenuItem;

public class UpdateMenuItemCommand : ICommand<UpdateMenuItemResult>
{
    public Guid Id { get; set; }
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

public record UpdateMenuItemResult(bool Success);

public class UpdateMenuItemCommandValidator : AbstractValidator<UpdateMenuItemCommand>
{
    public UpdateMenuItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(255).WithMessage("Name must not exceed 255 characters");
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).WithMessage("BasePrice must be greater than or equal to 0");
    }
}

internal class UpdateMenuItemCommandHandler(CatalogDbContext dbContext) : ICommandHandler<UpdateMenuItemCommand, UpdateMenuItemResult>
{
    public async Task<UpdateMenuItemResult> Handle(UpdateMenuItemCommand command, CancellationToken cancellationToken)
    {
        var menuItem = await dbContext.MenuItems
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);

        if (menuItem is null)
        {
            throw new NotFoundException(nameof(MenuItem), command.Id);
        }

        menuItem.SubCategoryId = command.SubCategoryId;
        menuItem.Name = command.Name;
        menuItem.Description = command.Description;
        menuItem.BasePrice = command.BasePrice;
        menuItem.ImageUrl = command.ImageUrl;
        menuItem.PrepTimeMinutes = command.PrepTimeMinutes;
        menuItem.PrepTimeMaxMinutes = command.PrepTimeMaxMinutes;
        menuItem.ItemType = command.ItemType;
        menuItem.IsAvailable = command.IsAvailable;
        menuItem.AvailabilityStatus = command.AvailabilityStatus;
        menuItem.SeasonStartDate = command.SeasonStartDate;
        menuItem.SeasonEndDate = command.SeasonEndDate;
        menuItem.PromoPrice = command.PromoPrice;
        menuItem.PromoStartDate = command.PromoStartDate;
        menuItem.PromoEndDate = command.PromoEndDate;
        menuItem.DisplayOrder = command.DisplayOrder;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateMenuItemResult(true);
    }
}

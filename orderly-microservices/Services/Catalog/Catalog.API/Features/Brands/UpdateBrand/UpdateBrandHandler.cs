namespace Catalog.API.Features.Brands.UpdateBrand;

public record UpdateBrandCommand(
    Guid Id,
    string Name,
    string Description,
    string LogoUrl,
    string WebsiteUrl,
    string ContactEmail,
    string ContactPhone,
    CuisineType CuisineType) : ICommand<UpdateBrandResult>;

public record UpdateBrandResult(bool IsSuccess);

public class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");
        RuleFor(x => x.LogoUrl)
            .NotEmpty().WithMessage("Logo is required")
            .MaximumLength(100).WithMessage("Logo must not exceed 100 characters");
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters");
        RuleFor(x => x.ContactEmail)
            .EmailAddress().WithMessage("ContactEmail must be a valid email address")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

internal class UpdateBrandCommandHandler(CatalogDbContext dbContext) : ICommandHandler<UpdateBrandCommand, UpdateBrandResult>
{
    public async Task<UpdateBrandResult> Handle(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await dbContext.Brands.FindAsync([command.Id], cancellationToken)
            ?? throw new BrandNotFoundException(command.Id);

        brand.Name = command.Name;
        brand.Description = command.Description;
        brand.LogoUrl = command.LogoUrl;
        brand.WebsiteUrl = command.WebsiteUrl;
        brand.ContactEmail = command.ContactEmail;
        brand.ContactPhone = command.ContactPhone;
        brand.CuisineType = command.CuisineType;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateBrandResult(true);
    }
}

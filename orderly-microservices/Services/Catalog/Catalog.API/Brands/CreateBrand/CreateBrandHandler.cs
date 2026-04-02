namespace Catalog.API.Brands.CreateBrand;

public record CreateBrandCommand(
    string Name,
    string Description,
    string LogoUrl,
    string WebsiteUrl,
    string ContactEmail,
    string ContactPhone,
    CuisineType CuisineType,
    bool IsActive) : ICommand<CreateBrandResult>;

public record CreateBrandResult(Guid Id);

public class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");
        RuleFor(x => x.LogoUrl)
            .NotEmpty().WithMessage("Logo is required")
            .MaximumLength(100).WithMessage("Logo must not exceed 100 characters");
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters");
        // Contact Email throws a "ContactEmail is required" validation error if Contact Phone is empty.
        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("ContactEmail is required when ContactPhone is empty")
            .When(x => string.IsNullOrWhiteSpace(x.ContactPhone));
        // Contact Phone similarly expects input if Contact Email is empty.
        RuleFor(x => x.ContactPhone)
            .NotEmpty().WithMessage("ContactPhone is required when ContactEmail is empty")
            .When(x => string.IsNullOrWhiteSpace(x.ContactEmail));
        // If an email is provided, I conditionally validate that it's a correctly formatted email address.
        RuleFor(x => x.ContactEmail)
            .EmailAddress().WithMessage("ContactEmail must be a valid email address")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

public class CreateBrandCommandHandler(IDocumentSession session) : ICommandHandler<CreateBrandCommand, CreateBrandResult>
{
    public async Task<CreateBrandResult> Handle(CreateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            LogoUrl = command.LogoUrl,
            WebsiteUrl = command.WebsiteUrl,
            ContactEmail = command.ContactEmail,
            ContactPhone = command.ContactPhone,
            CuisineType = command.CuisineType,
            IsActive = command.IsActive
        };

        if (brand is IAuditableEntity auditableEntity)
        {
            auditableEntity.CreatedFrom("system", SystemClock.Instance.GetCurrentInstant());
        }

        session.Store(brand);
        await session.SaveChangesAsync(cancellationToken);

        return new CreateBrandResult(brand.Id);
    }
}

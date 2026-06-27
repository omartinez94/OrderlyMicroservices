namespace Catalog.API.Features.Brands.DeleteBrand;

public record DeleteBrandCommand(Guid Id) : ICommand<DeleteBrandResult>;

public record DeleteBrandResult(bool IsSuccess);

internal class DeleteBrandCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteBrandCommand, DeleteBrandResult>
{
    public async Task<DeleteBrandResult> Handle(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await dbContext.Brands.FindAsync([command.Id], cancellationToken)
            ?? throw new BrandNotFoundException(command.Id);

        dbContext.Brands.Remove(brand);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteBrandResult(true);
    }
}

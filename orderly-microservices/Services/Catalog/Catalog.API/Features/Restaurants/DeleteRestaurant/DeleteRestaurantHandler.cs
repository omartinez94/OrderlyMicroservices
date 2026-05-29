namespace Catalog.API.Features.Restaurants.DeleteRestaurant;

public record DeleteRestaurantCommand(Guid Id) : ICommand<DeleteRestaurantResult>;

public record DeleteRestaurantResult(bool IsSuccess);

public class DeleteRestaurantCommandHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteRestaurantCommand, DeleteRestaurantResult>
{
    public async Task<DeleteRestaurantResult> Handle(DeleteRestaurantCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await dbContext.Restaurants.FindAsync([command.Id], cancellationToken);
        if (restaurant is null)
        {
            throw new RestaurantNotFoundException(command.Id);
        }

        dbContext.Restaurants.Remove(restaurant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeleteRestaurantResult(true);
    }
}

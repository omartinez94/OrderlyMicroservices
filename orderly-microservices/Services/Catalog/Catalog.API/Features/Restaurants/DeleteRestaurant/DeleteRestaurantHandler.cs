namespace Catalog.API.Features.Restaurants.DeleteRestaurant;

public record DeleteRestaurantCommand(Guid Id) : ICommand<DeleteRestaurantResult>;

public record DeleteRestaurantResult(bool IsSuccess);

public class DeleteRestaurantCommandHandler(IDocumentSession session) : ICommandHandler<DeleteRestaurantCommand, DeleteRestaurantResult>
{
    public async Task<DeleteRestaurantResult> Handle(DeleteRestaurantCommand command, CancellationToken cancellationToken)
    {
        session.Delete<Restaurant>(command.Id);
        await session.SaveChangesAsync(cancellationToken);

        return new DeleteRestaurantResult(true);
    }
}

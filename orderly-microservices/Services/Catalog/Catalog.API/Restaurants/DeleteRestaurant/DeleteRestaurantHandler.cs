namespace Catalog.API.Restaurants.DeleteRestaurant;

public record DeleteRestaurantCommand(Guid Id) : ICommand<DeleteRestaurantResult>;

public record DeleteRestaurantResult(bool IsSuccess);

public class DeleteRestaurantCommandHandler(IDocumentSession session, ILogger<DeleteRestaurantCommandHandler> logger) : ICommandHandler<DeleteRestaurantCommand, DeleteRestaurantResult>
{
    public async Task<DeleteRestaurantResult> Handle(DeleteRestaurantCommand command, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Deleting restaurant with Id: {Id}", command.Id);
        }

        session.Delete<Restaurant>(command.Id);
        await session.SaveChangesAsync(cancellationToken);

        return new DeleteRestaurantResult(true);
    }
}

namespace Identity.API.Features.Users.AssignRestaurants;

public record AssignRestaurantsCommand(Guid UserId, List<RestaurantAssignment> Restaurants) : ICommand<AssignRestaurantsResponse>;

public record RestaurantAssignment(int RestaurantId, bool IsDefault);

public record AssignRestaurantsResponse(Guid UserId, List<RestaurantAssignment> Restaurants);

public class AssignRestaurantsCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<AssignRestaurantsCommand, AssignRestaurantsResponse>
{
    public async Task<AssignRestaurantsResponse> Handle(AssignRestaurantsCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([command.UserId], cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        var existingRestaurants = dbContext.UserRestaurants.Where(ur => ur.UserId == command.UserId);
        dbContext.UserRestaurants.RemoveRange(existingRestaurants);

        var userRestaurants = command.Restaurants.Select(r => new UserRestaurant
        {
            UserId = command.UserId,
            User = user,
            RestaurantId = r.RestaurantId,
            IsDefault = r.IsDefault
        }).ToList();

        dbContext.UserRestaurants.AddRange(userRestaurants);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignRestaurantsResponse(command.UserId, command.Restaurants);
    }
}

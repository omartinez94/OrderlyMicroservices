namespace Identity.API.Features.Users.DeleteUser;

public record DeleteUserCommand(Guid UserId) : ICommand;

public class DeleteUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<DeleteUserCommand>
{
    public async Task<Unit> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        ApplicationUser user = (await dbContext.Users.FindAsync([command.UserId], cancellationToken))
            .MustExist("User", command.UserId);

        IdentityResult? result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            string? errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"User deletion failed: {errors}");
        }

        IQueryable<UserRestaurant>? userRestaurants = dbContext.UserRestaurants.Where(ur => ur.UserId == command.UserId);
        dbContext.UserRestaurants.RemoveRange(userRestaurants);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

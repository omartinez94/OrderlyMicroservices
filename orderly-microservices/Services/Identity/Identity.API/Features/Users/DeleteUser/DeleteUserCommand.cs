namespace Identity.API.Features.Users.DeleteUser;

public record DeleteUserCommand(Guid UserId) : ICommand;

public class DeleteUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<DeleteUserCommand>
{
    public async Task<Unit> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([command.UserId], cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"User deletion failed: {errors}");
        }

        var userRestaurants = dbContext.UserRestaurants.Where(ur => ur.UserId == command.UserId);
        dbContext.UserRestaurants.RemoveRange(userRestaurants);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

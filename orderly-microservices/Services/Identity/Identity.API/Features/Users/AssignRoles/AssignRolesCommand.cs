namespace Identity.API.Features.Users.AssignRoles;

public record AssignRolesCommand(Guid UserId, List<string> Roles) : ICommand<AssignRolesResponse>;

public record AssignRolesResponse(Guid UserId, List<string> Roles);

public class AssignRolesCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<AssignRolesCommand, AssignRolesResponse>
{
    public async Task<AssignRolesResponse> Handle(AssignRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([command.UserId], cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);

        await userManager.AddToRolesAsync(user, command.Roles);

        return new AssignRolesResponse(user.Id, command.Roles);
    }
}

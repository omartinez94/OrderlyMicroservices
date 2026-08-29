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
        ApplicationUser user = (await dbContext.Users.FindAsync([command.UserId], cancellationToken))
            .MustExist("User", command.UserId);

        IList<string>? currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);

        await userManager.AddToRolesAsync(user, command.Roles);

        return new AssignRolesResponse(user.Id, command.Roles);
    }
}

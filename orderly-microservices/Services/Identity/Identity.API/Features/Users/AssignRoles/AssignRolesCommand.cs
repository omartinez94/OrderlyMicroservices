namespace Identity.API.Features.Users.AssignRoles;

public record AssignRolesCommand(Guid UserId, List<string> Roles, ClaimsPrincipal? Caller = null) : ICommand<AssignRolesResponse>;

public record AssignRolesResponse(Guid UserId, List<string> Roles);

public class AssignRolesCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<AssignRolesCommand, AssignRolesResponse>
{
    public async Task<AssignRolesResponse> Handle(AssignRolesCommand command, CancellationToken cancellationToken)
    {
        // Privilege escalation guard: Only an existing SuperAdmin can assign or alter the SuperAdmin role.
        bool assigningSuperAdmin = command.Roles.Any(r => string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));
        if (assigningSuperAdmin)
        {
            bool callerIsSuperAdmin = command.Caller?.IsInRole("SuperAdmin") == true
                || command.Caller?.HasClaim(ClaimTypes.Role, "SuperAdmin") == true
                || command.Caller?.HasClaim("role", "SuperAdmin") == true;

            if (!callerIsSuperAdmin)
            {
                throw new ForbiddenException("Only SuperAdmin can assign the SuperAdmin role.");
            }
        }

        ApplicationUser user = (await dbContext.Users.FindAsync([command.UserId], cancellationToken))
            .MustExist("User", command.UserId);

        IList<string>? currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);

        await userManager.AddToRolesAsync(user, command.Roles);

        return new AssignRolesResponse(user.Id, command.Roles);
    }
}

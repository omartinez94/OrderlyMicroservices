namespace Identity.API.Features.Roles.UpdateRole;

public record UpdateRoleCommand(Guid RoleId, string Name, string? Description) : ICommand<UpdateRoleResponse>;

public record UpdateRoleResponse(Guid RoleId, string Name, string? Description);

public class UpdateRoleCommandHandler(RoleManager<ApplicationRole> roleManager)
    : ICommandHandler<UpdateRoleCommand, UpdateRoleResponse>
{
    public async Task<UpdateRoleResponse> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByIdAsync(command.RoleId.ToString());
        if (role is null)
        {
            throw new NotFoundException("Role", command.RoleId);
        }

        role.Name = command.Name;
        role.NormalizedName = command.Name.ToUpperInvariant();
        role.Description = command.Description;

        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"Role update failed: {errors}");
        }

        return new UpdateRoleResponse(role.Id, role.Name!, role.Description);
    }
}

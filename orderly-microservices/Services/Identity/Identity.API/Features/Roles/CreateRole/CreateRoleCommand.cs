namespace Identity.API.Features.Roles.CreateRole;

public record CreateRoleCommand(string Name, string? Description) : ICommand<CreateRoleResponse>;

public record CreateRoleResponse(Guid RoleId, string Name, string? Description);

public class CreateRoleCommandHandler(RoleManager<ApplicationRole> roleManager)
    : ICommandHandler<CreateRoleCommand, CreateRoleResponse>
{
    public async Task<CreateRoleResponse> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var existingRole = await roleManager.FindByNameAsync(command.Name);
        if (existingRole is not null)
        {
            throw new BadRequestException("Role with this name already exists.");
        }

        var role = new ApplicationRole
        {
            Name = command.Name,
            NormalizedName = command.Name.ToUpperInvariant(),
            Description = command.Description
        };

        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"Role creation failed: {errors}");
        }

        return new CreateRoleResponse(role.Id, role.Name!, role.Description);
    }
}

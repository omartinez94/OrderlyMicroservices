namespace Identity.API.Features.Permissions.AssignPermissions;

public record AssignPermissionsToRoleCommand(Guid RoleId, List<Guid> PermissionIds) : ICommand<AssignPermissionsToRoleResponse>;

public record AssignPermissionsToRoleResponse(Guid RoleId, List<Guid> PermissionIds);

public class AssignPermissionsToRoleCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    IdentityDbContext dbContext)
    : ICommandHandler<AssignPermissionsToRoleCommand, AssignPermissionsToRoleResponse>
{
    public async Task<AssignPermissionsToRoleResponse> Handle(AssignPermissionsToRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByIdAsync(command.RoleId.ToString());
        if (role is null)
        {
            throw new NotFoundException("Role", command.RoleId);
        }

        var existingPermissions = dbContext.RolePermissions.Where(rp => rp.RoleId == command.RoleId);
        dbContext.RolePermissions.RemoveRange(existingPermissions);

        var permissions = await dbContext.Permissions
            .Where(p => command.PermissionIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var rolePermissions = permissions.Select(p => new RolePermission
        {
            RoleId = command.RoleId,
            Role = role,
            PermissionId = p.Id,
            Permission = p
        }).ToList();

        dbContext.RolePermissions.AddRange(rolePermissions);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignPermissionsToRoleResponse(command.RoleId, command.PermissionIds);
    }
}

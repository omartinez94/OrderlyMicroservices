namespace Identity.API.Features.Roles.AssignPermissions;

public record AssignPermissionsCommand(Guid RoleId, List<Guid> PermissionIds) : ICommand<AssignPermissionsResponse>;

public record AssignPermissionsResponse(Guid RoleId, List<Guid> PermissionIds);

public class AssignPermissionsCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    IdentityDbContext dbContext)
    : ICommandHandler<AssignPermissionsCommand, AssignPermissionsResponse>
{
    public async Task<AssignPermissionsResponse> Handle(AssignPermissionsCommand command, CancellationToken cancellationToken)
    {
        var applicationRole = await roleManager.FindByIdAsync(command.RoleId.ToString());
        if (applicationRole is null)
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
            Role = applicationRole,
            PermissionId = p.Id,
            Permission = p
        }).ToList();

        dbContext.RolePermissions.AddRange(rolePermissions);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignPermissionsResponse(command.RoleId, command.PermissionIds);
    }
}

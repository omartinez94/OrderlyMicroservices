namespace Identity.API.Features.Roles.GetRole;

public record GetRoleQuery(Guid RoleId) : IQuery<GetRoleResponse>;

public record GetRoleResponse(Guid Id, string Name, string? Description, List<PermissionDto> Permissions);

public record PermissionDto(Guid Id, string Name, string Description, string Resource, string Action);

public class GetRoleQueryHandler(
    RoleManager<ApplicationRole> roleManager,
    IdentityDbContext dbContext)
    : IQueryHandler<GetRoleQuery, GetRoleResponse>
{
    public async Task<GetRoleResponse> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.FindAsync([query.RoleId], cancellationToken);
        if (role is null)
        {
            throw new NotFoundException("Role", query.RoleId);
        }

        var permissions = await dbContext.RolePermissions
            .Where(rp => rp.RoleId == query.RoleId)
            .Join(dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new PermissionDto(
                p.Id,
                p.Name,
                p.Description,
                p.Resource,
                p.Action))
            .ToListAsync(cancellationToken);

        return new GetRoleResponse(role.Id, role.Name!, role.Description, permissions);
    }
}

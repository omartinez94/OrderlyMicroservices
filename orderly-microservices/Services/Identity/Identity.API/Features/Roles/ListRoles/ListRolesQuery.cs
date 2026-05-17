namespace Identity.API.Features.Roles.ListRoles;

public record ListRolesQuery : IQuery<ListRolesResponse>;

public record ListRolesResponse(IEnumerable<RoleListItem> Roles);

public record RoleListItem(Guid Id, string Name, string? Description);

public class ListRolesQueryHandler(RoleManager<ApplicationRole> roleManager)
    : IQueryHandler<ListRolesQuery, ListRolesResponse>
{
    public async Task<ListRolesResponse> Handle(ListRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await roleManager.Roles
            .Select(r => new RoleListItem(r.Id, r.Name!, r.Description))
            .ToListAsync(cancellationToken);

        return new ListRolesResponse(roles);
    }
}

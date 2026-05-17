namespace Identity.API.Features.Permissions.ListPermissions;

public record ListPermissionsQuery : IQuery<ListPermissionsResponse>;

public record ListPermissionsResponse(IEnumerable<PermissionDto> Permissions);

public record PermissionDto(Guid Id, string Name, string Description, string Resource, string Action);

public class ListPermissionsQueryHandler(IdentityDbContext dbContext)
    : IQueryHandler<ListPermissionsQuery, ListPermissionsResponse>
{
    public async Task<ListPermissionsResponse> Handle(ListPermissionsQuery query, CancellationToken cancellationToken)
    {
        var permissions = await dbContext.Permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Description, p.Resource, p.Action))
            .ToListAsync(cancellationToken);

        return new ListPermissionsResponse(permissions);
    }
}

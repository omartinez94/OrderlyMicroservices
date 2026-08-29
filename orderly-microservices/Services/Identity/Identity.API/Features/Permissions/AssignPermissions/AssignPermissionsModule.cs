namespace Identity.API.Features.Permissions.AssignPermissions;

public class AssignPermissionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/permissions");

        group.MapPost("assign-to-role", async (
                ISender sender,
                AssignPermissionsToRoleRequest request,
                CancellationToken ct) =>
            {
                AssignPermissionsToRoleCommand? command = new AssignPermissionsToRoleCommand(request.RoleId, request.PermissionIds);
                AssignPermissionsToRoleResponse? response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:edit_permissions").Accepts<AssignPermissionsToRoleRequest>("application/json")
            .Produces<AssignPermissionsToRoleResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignPermissionsToRoleRequest(Guid RoleId, List<Guid> PermissionIds);


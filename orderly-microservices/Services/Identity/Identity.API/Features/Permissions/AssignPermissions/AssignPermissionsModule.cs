namespace Identity.API.Features.Permissions.AssignPermissions;

public class AssignPermissionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/permissions");

        group.MapPost("assign-to-role", async (
                ISender sender,
                AssignPermissionsToRoleRequest request,
                CancellationToken ct) =>
            {
                var command = new AssignPermissionsToRoleCommand(request.RoleId, request.PermissionIds);
                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:edit_permissions").Accepts<AssignPermissionsToRoleRequest>("application/json")
            .Produces<AssignPermissionsToRoleResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignPermissionsToRoleRequest(Guid RoleId, List<Guid> PermissionIds);


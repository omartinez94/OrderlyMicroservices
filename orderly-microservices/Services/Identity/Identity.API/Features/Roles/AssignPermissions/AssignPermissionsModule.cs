namespace Identity.API.Features.Roles.AssignPermissions;

public class AssignPermissionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles");

        group.MapPut("{id:guid}/permissions", async (
                Guid id,
                ISender sender,
                AssignPermissionsRequest request,
                CancellationToken ct) =>
            {
                var command = new AssignPermissionsCommand(id, request.PermissionIds);
                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:edit_permissions").Accepts<AssignPermissionsRequest>("application/json")
            .Produces<AssignPermissionsResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignPermissionsRequest(List<Guid> PermissionIds);


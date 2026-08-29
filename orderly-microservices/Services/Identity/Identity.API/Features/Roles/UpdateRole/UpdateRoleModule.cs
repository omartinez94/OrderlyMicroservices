namespace Identity.API.Features.Roles.UpdateRole;

public class UpdateRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/roles");

        group.MapPut("{id:guid}", async (
                Guid id,
                ISender sender,
                UpdateRoleRequest request,
                CancellationToken ct) =>
            {
                UpdateRoleCommand? command = new UpdateRoleCommand(id, request.Name, request.Description);
                UpdateRoleResponse? response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:edit").Accepts<UpdateRoleRequest>("application/json")
            .Produces<UpdateRoleResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(400);
    }
}

public record UpdateRoleRequest(string Name, string? Description = null);


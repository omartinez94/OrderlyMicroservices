namespace Identity.API.Features.Roles.UpdateRole;

public class UpdateRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles")
            .RequireAuthorization();

        group.MapPut("{id:guid}", async (
                Guid id,
                ISender sender,
                UpdateRoleRequest request,
                CancellationToken ct) =>
            {
                var command = new UpdateRoleCommand(id, request.Name, request.Description);
                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            })
            .Accepts<UpdateRoleRequest>("application/json")
            .Produces<UpdateRoleResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(400);
    }
}

public record UpdateRoleRequest(string Name, string? Description = null);

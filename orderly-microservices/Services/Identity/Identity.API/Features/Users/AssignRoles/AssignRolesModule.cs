namespace Identity.API.Features.Users.AssignRoles;

public class AssignRolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPut("{id:guid}/roles", async (
                Guid id,
                ISender sender,
                AssignRolesRequest request,
                CancellationToken ct) =>
            {
                var command = new AssignRolesCommand(id, request.Roles);
                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("users:assign_roles").Accepts<AssignRolesRequest>("application/json")
            .Produces<AssignRolesResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignRolesRequest(List<string> Roles);


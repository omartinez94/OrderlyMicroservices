namespace Identity.API.Features.Users.AssignRoles;

public class AssignRolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/users");

        group.MapPut("{id:guid}/roles", async (
                Guid id,
                ISender sender,
                AssignRolesRequest request,
                CancellationToken ct) =>
            {
                AssignRolesCommand? command = new AssignRolesCommand(id, request.Roles);
                AssignRolesResponse? response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("users:assign_roles").Accepts<AssignRolesRequest>("application/json")
            .Produces<AssignRolesResponse>(200)
            .ProducesProblem(404);
    }
}

public record AssignRolesRequest(List<string> Roles);


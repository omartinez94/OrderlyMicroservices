namespace Identity.API.Features.Roles.CreateRole;

public class CreateRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/roles");

        group.MapPost("", async (
                ISender sender,
                CreateRoleRequest request,
                CancellationToken ct) =>
            {
                CreateRoleCommand? command = new CreateRoleCommand(request.Name, request.Description);
                CreateRoleResponse? response = await sender.Send(command, ct);

                return Results.Created($"/v1/roles/{response.RoleId}", response);
            }).RequirePermission("roles:create").Accepts<CreateRoleRequest>("application/json")
            .Produces<CreateRoleResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);
    }
}

public record CreateRoleRequest(string Name, string? Description = null);


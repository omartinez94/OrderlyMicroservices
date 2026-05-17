namespace Identity.API.Features.Roles.CreateRole;

public class CreateRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles");

        group.MapPost("", async (
                ISender sender,
                CreateRoleRequest request,
                CancellationToken ct) =>
            {
                var command = new CreateRoleCommand(request.Name, request.Description);
                var response = await sender.Send(command, ct);

                return Results.Created($"/api/roles/{response.RoleId}", response);
            }).RequirePermission("roles:create").Accepts<CreateRoleRequest>("application/json")
            .Produces<CreateRoleResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);
    }
}

public record CreateRoleRequest(string Name, string? Description = null);


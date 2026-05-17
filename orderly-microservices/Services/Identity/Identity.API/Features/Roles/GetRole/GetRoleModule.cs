namespace Identity.API.Features.Roles.GetRole;

public class GetRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles");

        group.MapGet("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new GetRoleQuery(id);
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:view").Produces<GetRoleResponse>(200).RequirePermission("roles:view").ProducesProblem(404);
    }
}


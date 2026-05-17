namespace Identity.API.Features.Roles.GetRole;

public class GetRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles")
            .RequireAuthorization();

        group.MapGet("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new GetRoleQuery(id);
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            })
            .Produces<GetRoleResponse>(200)
            .ProducesProblem(404);
    }
}

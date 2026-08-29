namespace Identity.API.Features.Roles.GetRole;

public class GetRoleModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/roles");

        group.MapGet("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                GetRoleQuery? query = new GetRoleQuery(id);
                GetRoleResponse? response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:view").Produces<GetRoleResponse>(200).RequirePermission("roles:view").ProducesProblem(404);
    }
}


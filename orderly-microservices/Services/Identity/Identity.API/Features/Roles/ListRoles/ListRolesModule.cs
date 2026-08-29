namespace Identity.API.Features.Roles.ListRoles;

public class ListRolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/roles");

        group.MapGet("", async (
                ISender sender,
                CancellationToken ct) =>
            {
                ListRolesQuery? query = new ListRolesQuery();
                ListRolesResponse? response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("roles:view").Produces<ListRolesResponse>(200);
    }
}


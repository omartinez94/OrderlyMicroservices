namespace Identity.API.Features.Roles.ListRoles;

public class ListRolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles")
            .RequireAuthorization();

        group.MapGet("", async (
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new ListRolesQuery();
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            })
            .Produces<ListRolesResponse>(200);
    }
}

namespace Identity.API.Features.Permissions.ListPermissions;

public class ListPermissionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/permissions")
            .RequireAuthorization();

        group.MapGet("", async (
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new ListPermissionsQuery();
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            })
            .Produces<ListPermissionsResponse>(200);
    }
}

namespace Identity.API.Features.Permissions.ListPermissions;

public class ListPermissionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/permissions");

        group.MapGet("", async (
                ISender sender,
                CancellationToken ct) =>
            {
                ListPermissionsQuery? query = new ListPermissionsQuery();
                ListPermissionsResponse? response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("permissions:view").Produces<ListPermissionsResponse>(200);
    }
}


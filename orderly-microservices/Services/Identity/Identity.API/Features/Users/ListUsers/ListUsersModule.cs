namespace Identity.API.Features.Users.ListUsers;

public class ListUsersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/users");

        group.MapGet("", async (
                [AsParameters] ListUsersRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                ListUsersQuery? query = new ListUsersQuery(request.Page, request.PageSize, request.SearchTerm);
                ListUsersResponse? response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("users:view_all").Produces<ListUsersResponse>(200);
    }
}

public record ListUsersRequest(int Page = 1, int PageSize = 50, string? SearchTerm = null);


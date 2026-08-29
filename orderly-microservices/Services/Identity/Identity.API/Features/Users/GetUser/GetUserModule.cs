namespace Identity.API.Features.Users.GetUser;

public class GetUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/users");

        group.MapGet("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                GetUserQuery? query = new GetUserQuery(id);
                GetUserResponse? response = await sender.Send(query, ct);

                return Results.Ok(response);
            }).RequirePermission("users:view_all").Produces<GetUserResponse>(200).RequirePermission("users:view_all").ProducesProblem(404);
    }
}


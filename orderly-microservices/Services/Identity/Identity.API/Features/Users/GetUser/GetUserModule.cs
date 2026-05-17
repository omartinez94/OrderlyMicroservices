namespace Identity.API.Features.Users.GetUser;

public class GetUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapGet("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var query = new GetUserQuery(id);
                var response = await sender.Send(query, ct);

                return Results.Ok(response);
            })
            .Produces<GetUserResponse>(200)
            .ProducesProblem(404);
    }
}

namespace Identity.API.Features.Users.DeleteUser;

public class DeleteUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapDelete("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var command = new DeleteUserCommand(id);
                await sender.Send(command, ct);

                return Results.NoContent();
            })
            .Produces(204)
            .ProducesProblem(404);
    }
}

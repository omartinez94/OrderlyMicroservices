namespace Identity.API.Features.Users.DeleteUser;

public class DeleteUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapDelete("{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
            {
                var command = new DeleteUserCommand(id);
                await sender.Send(command, ct);

                return Results.NoContent();
            }).RequirePermission("users:delete").Produces(204).RequirePermission("users:delete").ProducesProblem(404);
    }
}


namespace Identity.API.Features.Users.UpdateUser;

public class UpdateUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPut("{id:guid}", async (
                Guid id,
                ISender sender,
                UpdateUserRequest request,
                CancellationToken ct) =>
            {
                var command = new UpdateUserCommand(
                    id,
                    request.FirstName,
                    request.LastName,
                    request.PhoneNumber,
                    request.IsActive);

                var response = await sender.Send(command, ct);

                return Results.Ok(response);
            }).RequirePermission("users:edit").Accepts<UpdateUserRequest>("application/json")
            .Produces<UpdateUserResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(400);
    }
}

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    bool IsActive = true);


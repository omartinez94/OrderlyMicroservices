namespace Identity.API.Features.Users.CreateUser;

public class CreateUserModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization();

        group.MapPost("", async (
                ISender sender,
                CreateUserRequest request,
                CancellationToken ct) =>
            {
                var command = new CreateUserCommand(
                    request.Email,
                    request.Password,
                    request.FirstName,
                    request.LastName,
                    request.PhoneNumber,
                    request.Roles,
                    request.RestaurantIds,
                    request.DefaultRestaurantId);

                var response = await sender.Send(command, ct);

                return Results.Created($"/api/users/{response.UserId}", response);
            })
            .Accepts<CreateUserRequest>("application/json")
            .Produces<CreateUserResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);
    }
}

public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    List<string>? Roles = null,
    List<int>? RestaurantIds = null,
    int? DefaultRestaurantId = null)
{
    public List<string> Roles { get; init; } = Roles ?? [];
    public List<int> RestaurantIds { get; init; } = RestaurantIds ?? [];
}

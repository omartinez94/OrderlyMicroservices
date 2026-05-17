namespace Identity.API.Features.Auth.Register;

public class RegisterModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (
                ISender sender,
                RegisterRequest request,
                CancellationToken ct) =>
            {
                var command = new RegisterCommand(request);
                var response = await sender.Send(command, ct);

                return Results.Created($"/api/users/{response.UserId}", response);
            })
            .Accepts<RegisterRequest>("application/json")
            .Produces<RegisterResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);
    }
}

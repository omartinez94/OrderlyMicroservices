namespace Identity.API.Features.Auth.Register;

public class RegisterModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1/auth");

        group.MapPost("/register", async (
                ISender sender,
                RegisterRequest request,
                CancellationToken ct) =>
            {
                RegisterCommand? command = new RegisterCommand(request);
                RegisterResponse response = await sender.Send(command, ct);

                return Results.Created($"/v1/users/{response.UserId}", response);
            })
            .Accepts<RegisterRequest>("application/json")
            .Produces<RegisterResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .RequireRateLimiting(RateLimitPolicies.Auth);
    }
}


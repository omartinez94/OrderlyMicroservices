namespace Identity.API.Features.Auth.Register;

public class RegisterModule : CarterModule
{
    public RegisterModule() : base("/api/auth") { }

    public override void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/register", async (
                UserManager<ApplicationUser> userManager,
                RegisterRequest request,
                CancellationToken ct) =>
            {
                var existingUser = await userManager.FindByEmailAsync(request.Email);
                if (existingUser is not null)
                {
                    return Results.Conflict("User with this email already exists.");
                }

                var user = new ApplicationUser
                {
                    UserName = request.Email,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    PhoneNumber = request.PhoneNumber,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    return Results.BadRequest(result.Errors.Select(e => e.Description));
                }

                var response = new RegisterResponse(
                    user.Id,
                    user.Email!,
                    user.FirstName,
                    user.LastName);

                return Results.Created($"/api/users/{user.Id}", response);
            })
            .Accepts<RegisterRequest>("application/json")
            .Produces<RegisterResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(409);
    }
}

namespace Identity.API.Features.Auth.Register;

public record RegisterCommand(RegisterRequest Request) : ICommand<RegisterResponse>;

public class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    AuditLogger auditLogger) 
    : ICommandHandler<RegisterCommand, RegisterResponse>
{
    public async Task<RegisterResponse> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var existingUser = await userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
        {
            throw new BadRequestException("User with this email already exists.");
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
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"Registration failed: {errors}");
        }

        await auditLogger.LogAsync(
            user.Id,
            "RegisterSuccess",
            "N/A", // We'll need a way to get IP/UserAgent if we want it here
            "N/A",
            "User registered successfully",
            cancellationToken);

        return new RegisterResponse(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName);
    }
}

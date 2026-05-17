namespace Identity.API.Features.Users.UpdateUser;

public record UpdateUserCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive) : ICommand<UpdateUserResponse>;

public record UpdateUserResponse(Guid UserId, string Email, string FirstName, string LastName, bool IsActive);

public class UpdateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<UpdateUserCommand, UpdateUserResponse>
{
    public async Task<UpdateUserResponse> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([command.UserId], cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.PhoneNumber = command.PhoneNumber;
        user.IsActive = command.IsActive;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"User update failed: {errors}");
        }

        return new UpdateUserResponse(user.Id, user.Email!, user.FirstName, user.LastName, user.IsActive);
    }
}

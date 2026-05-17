namespace Identity.API.Features.Users.CreateUser;

public record CreateUserCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    List<string> Roles,
    List<int> RestaurantIds,
    int? DefaultRestaurantId) : ICommand<CreateUserResponse>;

public record CreateUserResponse(Guid UserId, string Email, string FirstName, string LastName);

public class CreateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : ICommandHandler<CreateUserCommand, CreateUserResponse>
{
    public async Task<CreateUserResponse> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var existingUser = await userManager.FindByEmailAsync(command.Email);
        if (existingUser is not null)
        {
            throw new BadRequestException("User with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = command.Email,
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            PhoneNumber = command.PhoneNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"User creation failed: {errors}");
        }

        if (command.Roles.Any())
        {
            await userManager.AddToRolesAsync(user, command.Roles);
        }

        if (command.RestaurantIds.Any())
        {
            var userRestaurants = command.RestaurantIds.Select(rid => new UserRestaurant
            {
                UserId = user.Id,
                User = user,
                RestaurantId = rid,
                IsDefault = command.DefaultRestaurantId == rid || 
                           (command.DefaultRestaurantId == null && rid == command.RestaurantIds.First())
            });
            dbContext.UserRestaurants.AddRange(userRestaurants);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateUserResponse(user.Id, user.Email!, user.FirstName, user.LastName);
    }
}

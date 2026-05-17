namespace Identity.API.Features.Users.GetUser;

public record GetUserQuery(Guid UserId) : IQuery<GetUserResponse>;

public record GetUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    List<string> Roles,
    List<UserRestaurantResponse> Restaurants);

public record UserRestaurantResponse(int RestaurantId, bool IsDefault);

public class GetUserQueryHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : IQueryHandler<GetUserQuery, GetUserResponse>
{
    public async Task<GetUserResponse> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([query.UserId], cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User", query.UserId);
        }

        var roles = await userManager.GetRolesAsync(user);

        var userRestaurants = await dbContext.UserRestaurants
            .Where(ur => ur.UserId == query.UserId)
            .Select(ur => new UserRestaurantResponse(ur.RestaurantId, ur.IsDefault))
            .ToListAsync(cancellationToken);

        return new GetUserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            roles.ToList(),
            userRestaurants);
    }
}

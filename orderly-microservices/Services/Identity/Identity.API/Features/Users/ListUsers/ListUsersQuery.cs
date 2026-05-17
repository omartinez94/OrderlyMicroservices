namespace Identity.API.Features.Users.ListUsers;

public record ListUsersQuery(int Page = 1, int PageSize = 50, string? SearchTerm = null) : IQuery<ListUsersResponse>;

public record ListUsersResponse(IEnumerable<UserListItem> Users, int TotalCount);

public record UserListItem(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    List<string> Roles);

public class ListUsersQueryHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext)
    : IQueryHandler<ListUsersQuery, ListUsersResponse>
{
    public async Task<ListUsersResponse> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var usersQuery = dbContext.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            usersQuery = usersQuery.Where(u =>
                u.Email!.Contains(query.SearchTerm) ||
                u.FirstName.Contains(query.SearchTerm) ||
                u.LastName.Contains(query.SearchTerm));
        }

        var totalCount = await usersQuery.CountAsync(cancellationToken);

        var users = await usersQuery
            .OrderBy(u => u.LastName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var userItems = new List<UserListItem>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            userItems.Add(new UserListItem(
                user.Id,
                user.Email ?? string.Empty,
                user.FirstName,
                user.LastName,
                user.IsActive,
                user.LastLoginAt,
                roles.ToList()));
        }

        return new ListUsersResponse(userItems, totalCount);
    }
}

using Hangfire.Dashboard;

namespace Catalog.API.Scheduling;

/// <summary>
/// Hangfire dashboard authorization filter — only authenticated users with
/// the <c>Admin</c> role claim may view / trigger / delete jobs.
/// </summary>
public sealed class HangfireAdminOnlyFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return user.IsInRole(nameof(Role.Admin)) || user.IsInRole(nameof(Role.Manager));
    }
}
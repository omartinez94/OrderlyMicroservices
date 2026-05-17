using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Authorization;

public class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
    {
        Policy = $"Permission:{permission}";
    }
}

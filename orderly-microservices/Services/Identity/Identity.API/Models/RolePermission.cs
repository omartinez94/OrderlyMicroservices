namespace Identity.API.Models;

public class RolePermission
{
    public required Guid RoleId { get; set; }
    public required ApplicationRole Role { get; set; }
    public required Guid PermissionId { get; set; }
    public required Permission Permission { get; set; }
}

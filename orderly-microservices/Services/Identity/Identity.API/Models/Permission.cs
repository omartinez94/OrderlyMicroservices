namespace Identity.API.Models;

public class Permission
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Resource { get; set; }
    public required string Action { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

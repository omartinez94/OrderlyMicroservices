namespace Identity.API.Data;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) 
    : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<UserRestaurant> UserRestaurants => Set<UserRestaurant>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}

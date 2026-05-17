namespace Identity.API.Data;

public static class DataSeeder
{
    public static async Task SeedDataAsync(IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await dbContext.Database.MigrateAsync(ct);

        await SeedRolesAsync(roleManager, ct);
        await SeedPermissionsAsync(dbContext, ct);
        await SeedRolePermissionsAsync(dbContext, roleManager, ct);
        await SeedSuperAdminAsync(userManager, roleManager, ct);

        await dbContext.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager, CancellationToken ct)
    {
        var roles = new[]
        {
            new { Name = "SuperAdmin", Description = "System-wide control, manage all restaurants" },
            new { Name = "RestaurantAdmin", Description = "Full control within assigned restaurant(s)" },
            new { Name = "Manager", Description = "Operational management, approve modifications, view reports" },
            new { Name = "KitchenManager", Description = "Kitchen oversight, manage kitchen staff" },
            new { Name = "Waiter", Description = "Create/modify orders (limited by status)" },
            new { Name = "KitchenStaff", Description = "View orders, update prep status" },
            new { Name = "Host", Description = "Manage reservations, assign tables, walk-in queue" },
            new { Name = "Cashier", Description = "Process payments, split bills" }
        };

        foreach (var roleData in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleData.Name))
            {
                var role = new ApplicationRole
                {
                    Name = roleData.Name,
                    NormalizedName = roleData.Name.ToUpperInvariant(),
                    Description = roleData.Description
                };
                await roleManager.CreateAsync(role);
            }
        }
    }

    private static async Task SeedPermissionsAsync(IdentityDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.Permissions.AnyAsync(ct))
            return;

        var permissions = new List<Permission>
        {
            // Users
            new() { Id = Guid.NewGuid(), Name = "users:view_all", Description = "View all users", Resource = "users", Action = "view_all" },
            new() { Id = Guid.NewGuid(), Name = "users:create", Description = "Create users", Resource = "users", Action = "create" },
            new() { Id = Guid.NewGuid(), Name = "users:edit", Description = "Edit users", Resource = "users", Action = "edit" },
            new() { Id = Guid.NewGuid(), Name = "users:delete", Description = "Delete users", Resource = "users", Action = "delete" },
            new() { Id = Guid.NewGuid(), Name = "users:assign_roles", Description = "Assign roles to users", Resource = "users", Action = "assign_roles" },
            new() { Id = Guid.NewGuid(), Name = "users:assign_restaurants", Description = "Assign restaurants to users", Resource = "users", Action = "assign_restaurants" },
            
            // Roles
            new() { Id = Guid.NewGuid(), Name = "roles:view", Description = "View roles", Resource = "roles", Action = "view" },
            new() { Id = Guid.NewGuid(), Name = "roles:create", Description = "Create roles", Resource = "roles", Action = "create" },
            new() { Id = Guid.NewGuid(), Name = "roles:edit", Description = "Edit roles", Resource = "roles", Action = "edit" },
            new() { Id = Guid.NewGuid(), Name = "roles:edit_permissions", Description = "Edit role permissions", Resource = "roles", Action = "edit_permissions" },
            
            // Permissions
            new() { Id = Guid.NewGuid(), Name = "permissions:view", Description = "View permissions", Resource = "permissions", Action = "view" },
            
            // Orders
            new() { Id = Guid.NewGuid(), Name = "orders:create", Description = "Create orders", Resource = "orders", Action = "create" },
            new() { Id = Guid.NewGuid(), Name = "orders:view_own", Description = "View own orders", Resource = "orders", Action = "view_own" },
            new() { Id = Guid.NewGuid(), Name = "orders:view_all", Description = "View all orders", Resource = "orders", Action = "view_all" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_ordering", Description = "Modify orders in ordering status", Resource = "orders", Action = "modify_ordering" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_confirmed", Description = "Modify orders in confirmed status", Resource = "orders", Action = "modify_confirmed" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_ready", Description = "Modify orders in ready status", Resource = "orders", Action = "modify_ready" },
            
            // Menu
            new() { Id = Guid.NewGuid(), Name = "menu:view", Description = "View menu", Resource = "menu", Action = "view" },
            new() { Id = Guid.NewGuid(), Name = "menu:edit", Description = "Edit menu", Resource = "menu", Action = "edit" },
            
            // Kitchen
            new() { Id = Guid.NewGuid(), Name = "kitchen:view_orders", Description = "View kitchen orders", Resource = "kitchen", Action = "view_orders" },
            new() { Id = Guid.NewGuid(), Name = "kitchen:update_prep_status", Description = "Update prep status", Resource = "kitchen", Action = "update_prep_status" },
            
            // Reservations
            new() { Id = Guid.NewGuid(), Name = "reservations:view", Description = "View reservations", Resource = "reservations", Action = "view" },
            new() { Id = Guid.NewGuid(), Name = "reservations:create", Description = "Create reservations", Resource = "reservations", Action = "create" },
            new() { Id = Guid.NewGuid(), Name = "reservations:edit", Description = "Edit reservations", Resource = "reservations", Action = "edit" },
            
            // Payments
            new() { Id = Guid.NewGuid(), Name = "payments:process", Description = "Process payments", Resource = "payments", Action = "process" },
            new() { Id = Guid.NewGuid(), Name = "payments:split_bill", Description = "Split bills", Resource = "payments", Action = "split_bill" },
            new() { Id = Guid.NewGuid(), Name = "payments:view_reports", Description = "View payment reports", Resource = "payments", Action = "view_reports" },
            
            // Audit
            new() { Id = Guid.NewGuid(), Name = "audit:view", Description = "View audit log", Resource = "audit", Action = "view" },
        };

        dbContext.Permissions.AddRange(permissions);
    }

    private static async Task SeedRolePermissionsAsync(IdentityDbContext dbContext, RoleManager<ApplicationRole> roleManager, CancellationToken ct)
    {
        if (await dbContext.RolePermissions.AnyAsync(ct))
            return;

        var rolePermissionMap = new Dictionary<string, List<string>>
        {
            ["SuperAdmin"] = dbContext.Permissions.Select(p => p.Name).ToList(),
            ["RestaurantAdmin"] = new()
            {
                "users:view_all", "users:create", "users:edit", "users:assign_roles", "users:assign_restaurants",
                "roles:view", "roles:edit", "roles:edit_permissions",
                "permissions:view",
                "orders:view_all", "orders:modify_confirmed", "orders:modify_ready",
                "menu:view", "menu:edit",
                "kitchen:view_orders",
                "reservations:view", "reservations:create", "reservations:edit",
                "payments:view_reports",
                "audit:view"
            },
            ["Manager"] = new()
            {
                "users:view_all",
                "roles:view",
                "permissions:view",
                "orders:view_all", "orders:modify_confirmed",
                "menu:view", "menu:edit",
                "kitchen:view_orders",
                "reservations:view", "reservations:edit",
                "payments:view_reports",
                "audit:view"
            },
            ["KitchenManager"] = new()
            {
                "orders:view_all", "orders:modify_ordering", "orders:modify_confirmed",
                "kitchen:view_orders", "kitchen:update_prep_status"
            },
            ["Waiter"] = new()
            {
                "orders:create", "orders:view_own", "orders:modify_ordering",
                "menu:view",
                "reservations:view", "reservations:create"
            },
            ["KitchenStaff"] = new()
            {
                "orders:view_all",
                "kitchen:view_orders", "kitchen:update_prep_status"
            },
            ["Host"] = new()
            {
                "reservations:view", "reservations:create", "reservations:edit",
                "orders:view_all"
            },
            ["Cashier"] = new()
            {
                "orders:view_all",
                "payments:process", "payments:split_bill"
            }
        };

        foreach (var (roleName, permissionNames) in rolePermissionMap)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            var permissions = await dbContext.Permissions
                .Where(p => permissionNames.Contains(p.Name))
                .ToListAsync(ct);

            var rolePermissions = permissions.Select(p => new RolePermission
            {
                RoleId = role.Id,
                Role = role,
                PermissionId = p.Id,
                Permission = p
            });

            dbContext.RolePermissions.AddRange(rolePermissions);
        }
    }

    private static async Task SeedSuperAdminAsync(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, CancellationToken ct)
    {
        var adminEmail = "admin@orderly.com";
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is not null)
            return;

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "System",
            LastName = "Administrator",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, "Admin@123456");
        if (result.Succeeded)
        {
            var role = await roleManager.FindByNameAsync("SuperAdmin");
            if (role is not null)
            {
                await userManager.AddToRoleAsync(admin, "SuperAdmin");
            }
        }
    }
}

namespace Identity.API.Data;

public static class DataSeeder
{
    public static async Task SeedDataAsync(
        IServiceProvider serviceProvider,
        IWebHostEnvironment environment,
        CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await dbContext.Database.MigrateAsync(ct);

        await SeedRolesAsync(roleManager, ct);
        await SeedPermissionsAsync(dbContext, ct);
        await SeedRolePermissionsAsync(dbContext, roleManager, ct);
        await SeedSuperAdminAsync(userManager, roleManager, environment, ct);
        await EnsureSuperAdminOrFailFastAsync(userManager, environment, ct);
        await SeedOpenIddictScopesAsync(scopeManager, ct);
        await SeedOpenIddictClientsAsync(applicationManager, configuration, ct);

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
            new() { Id = Guid.NewGuid(), Name = "orders:admin", Description = "Cross-account basket administration (CS / support tooling). Required for /api/v1/admin/carts/* endpoints (Basket Phase 4).", Resource = "orders", Action = "admin" },

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
                "orders:view_all", "orders:modify_confirmed", "orders:modify_ready", "orders:admin",
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

    /// <summary>
    /// Dev-only SuperAdmin seed. The hard-coded
    /// <c>admin@orderly.com / Admin@123456</c> credential is gated on
    /// <see cref="IHostEnvironment.IsDevelopment"/> per
    /// <c>TRUST_ROOT_HARDENING_PLAN.md §6.2</c> — production deploys
    /// must provision a SuperAdmin via an out-of-band bootstrap (CLI,
    /// migration, IaC) and the absence of one is detected by
    /// <see cref="EnsureSuperAdminOrFailFastAsync"/>.
    /// </summary>
    private static async Task SeedSuperAdminAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IWebHostEnvironment environment,
        CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return;

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

    /// <summary>
    /// Fail-fast guard for non-Development environments: a missing
    /// SuperAdmin row is unrecoverable (no first-login flow exists for
    /// the role), so we throw
    /// <see cref="MissingSuperAdminException"/> rather than booting a
    /// host that cannot be administered.
    /// </summary>
    private static async Task EnsureSuperAdminOrFailFastAsync(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment environment,
        CancellationToken ct)
    {
        if (environment.IsDevelopment())
            return;

        var superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");
        if (superAdmins.Count == 0)
        {
            throw new MissingSuperAdminException(
                "No SuperAdmin user exists in environment '" + environment.EnvironmentName + "'. " +
                "Provision one before starting the host: e.g. " +
                "INSERT INTO AspNetUsers (...) + INSERT INTO AspNetUserRoles (...) " +
                "or run `dotnet run --project tools/Orderly.Identity.AdminCli` (see docs/operations/bootstrap-superadmin.md).");
        }
    }

    /// <summary>
    /// Registers the OpenIddict scopes referenced by the seeded
    /// clients. Most OpenIddict built-in scopes are auto-registered
    /// on first startup, but to make the SPA's <c>offline_access</c>
    /// request work end-to-end we register it explicitly. The
    /// project-specific <c>restaurantId</c> and <c>internal</c> scopes
    /// must always be registered before a client can be granted them
    /// via <c>Permissions</c>.
    /// </summary>
    private static async Task SeedOpenIddictScopesAsync(
        IOpenIddictScopeManager scopeManager,
        CancellationToken ct)
    {
        var customScopes = new[]
        {
            (Name: "scp:offline_access", DisplayName: "Offline access", Description: "Allows the client to request refresh tokens for offline access."),
            (Name: "scp:restaurantId", DisplayName: "Restaurant ID", Description: "Restaurant (tenant) identifier associated with the authenticated user."),
            (Name: "scp:internal", DisplayName: "Internal M2M", Description: "Machine-to-machine scope for service-bus clients (bus-style service communication)."),
        };

        foreach (var (name, displayName, description) in customScopes)
        {
            if (await scopeManager.FindByNameAsync(name) is not null)
                continue;

            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                Resources = { "orderly-api" },
            }, ct);
        }
    }

    /// <summary>
    /// Idempotent seed of the SPA + M2M OpenIddict clients. Runs on
    /// every startup so a fresh database picks the clients up and a
    /// re-run over an existing database is a no-op (matched on
    /// <c>ClientId</c>).
    /// </summary>
    private static async Task SeedOpenIddictClientsAsync(
        IOpenIddictApplicationManager applicationManager,
        IConfiguration configuration,
        CancellationToken ct)
    {
        await SeedSpaClientAsync(applicationManager, configuration, ct);
        await SeedM2MClientAsync(applicationManager, configuration, ct);
    }

    private static async Task SeedSpaClientAsync(
        IOpenIddictApplicationManager applicationManager,
        IConfiguration configuration,
        CancellationToken ct)
    {
        const string clientId = "orderly-spa";

        if (await applicationManager.FindByClientIdAsync(clientId) is not null)
            return;

        var redirectUri = configuration["Spa:RedirectUri"]
            ?? throw new InvalidOperationException("Spa:RedirectUri is required to seed the orderly-spa OpenIddict client.");
        var postLogoutRedirectUri = configuration["Spa:PostLogoutRedirectUri"]
            ?? throw new InvalidOperationException("Spa:PostLogoutRedirectUri is required to seed the orderly-spa OpenIddict client.");

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = "Orderly SPA",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
        };

        descriptor.RedirectUris.Add(new Uri(redirectUri));
        descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri));

        // Endpoints the SPA can hit.
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);

        // Response type: authorization_code (with PKCE).
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        // Scopes the SPA can request. offline_access is not exposed as
        // an OpenIddictConstants.Permissions.Scopes.* constant in 7.5;
        // the standard string ("scp:offline_access" — see the registered
        // scope name below) is used.
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Email);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Roles);
        descriptor.Permissions.Add("scp:offline_access");
        descriptor.Permissions.Add("scp:restaurantId");

        // PKCE is mandatory for the SPA per the spec.
        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        await applicationManager.CreateAsync(descriptor, ct);
    }

    private static async Task SeedM2MClientAsync(
        IOpenIddictApplicationManager applicationManager,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var clientId = configuration["M2M:ClientId"]
            ?? throw new InvalidOperationException("M2M:ClientId is required to seed the M2M OpenIddict client.");

        if (await applicationManager.FindByClientIdAsync(clientId) is not null)
            return;

        var clientSecret = configuration["M2M:ClientSecret"]
            ?? throw new InvalidOperationException("M2M:ClientSecret is required to seed the M2M OpenIddict client.");

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = "Orderly Service Bus",
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Systematic,
        };

        // PBKDF2-hashed via the Secret property (don't store the raw value).
        descriptor.ClientSecret = clientSecret;

        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
        descriptor.Permissions.Add("scp:internal");

        await applicationManager.CreateAsync(descriptor, ct);
    }
}

namespace Identity.API.Data;

public static class DataSeeder
{
    public static async Task SeedDataAsync(
        IServiceProvider serviceProvider,
        IWebHostEnvironment environment,
        CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        IdentityDbContext? dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        RoleManager<ApplicationRole>? roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser>? userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IConfiguration? configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        IOpenIddictApplicationManager? applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        IOpenIddictScopeManager? scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        // Phase 2: schema application moved to IdentityMigratorHostedService
        // (registered in Program.cs). The hosted service runs at
        // IHostedService.StartAsync before this seeder runs, so the
        // schema is in place when the seed inserts begin. This is the
        // single source of truth for migration order on Identity.

        await SeedRolesAsync(roleManager, ct);
        await SeedPermissionsAsync(dbContext, ct);
        await SeedRolePermissionsAsync(dbContext, roleManager, ct);
        await SeedSuperAdminAsync(userManager, roleManager, environment, ct);
        await SeedAcmeUsersAsync(userManager, roleManager, dbContext, environment, ct);
        await EnsureSuperAdminOrFailFastAsync(userManager, environment, ct);
        await SeedOpenIddictScopesAsync(scopeManager, ct);
        await SeedOpenIddictClientsAsync(applicationManager, configuration, ct);

        await dbContext.SaveChangesAsync(ct);
    }

    public static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager, CancellationToken ct = default)
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
            new { Name = "Cashier", Description = "Process payments, split bills" },
        };

        foreach (var roleData in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleData.Name))
            {
                ApplicationRole? role = new ApplicationRole
                {
                    Name = roleData.Name,
                    NormalizedName = roleData.Name.ToUpperInvariant(),
                    Description = roleData.Description,
                };
                await roleManager.CreateAsync(role);
            }
        }
    }

    public static async Task SeedPermissionsAsync(IdentityDbContext dbContext, CancellationToken ct = default)
    {
        List<Permission> permissions = new List<Permission>
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
            new() { Id = Guid.NewGuid(), Name = "orders:write", Description = "Create or update orders", Resource = "orders", Action = "write" },
            new() { Id = Guid.NewGuid(), Name = "orders:cancel", Description = "Cancel orders directly", Resource = "orders", Action = "cancel" },
            new() { Id = Guid.NewGuid(), Name = "orders:request_cancel", Description = "Request order cancellation", Resource = "orders", Action = "request_cancel" },
            new() { Id = Guid.NewGuid(), Name = "orders:approve_cancel", Description = "Approve or reject order cancellation", Resource = "orders", Action = "approve_cancel" },
            new() { Id = Guid.NewGuid(), Name = "orders:view_own", Description = "View own orders", Resource = "orders", Action = "view_own" },
            new() { Id = Guid.NewGuid(), Name = "orders:view_all", Description = "View all orders", Resource = "orders", Action = "view_all" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_ordering", Description = "Modify orders in ordering status", Resource = "orders", Action = "modify_ordering" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_confirmed", Description = "Modify orders in confirmed status", Resource = "orders", Action = "modify_confirmed" },
            new() { Id = Guid.NewGuid(), Name = "orders:modify_ready", Description = "Modify orders in ready status", Resource = "orders", Action = "modify_ready" },
            new() { Id = Guid.NewGuid(), Name = "orders:admin", Description = "Cross-account basket administration (CS / support tooling). Required for /v1/admin/carts/* endpoints (Basket Phase 4).", Resource = "orders", Action = "admin" },

            // Menu
            new() { Id = Guid.NewGuid(), Name = "menu:view", Description = "View menu", Resource = "menu", Action = "view" },
            new() { Id = Guid.NewGuid(), Name = "menu:toggle", Description = "Toggle menu item availability (86'd / limited)", Resource = "menu", Action = "toggle" },
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

            // Dashboard
            new() { Id = Guid.NewGuid(), Name = "dashboard:view", Description = "View restaurant operations live dashboard", Resource = "dashboard", Action = "view" },

            // Restaurants & Tenant Provisioning
            new() { Id = Guid.NewGuid(), Name = "restaurants:create", Description = "Create new restaurant tenant", Resource = "restaurants", Action = "create" },
            new() { Id = Guid.NewGuid(), Name = "restaurants:edit", Description = "Edit restaurant configuration", Resource = "restaurants", Action = "edit" },
            new() { Id = Guid.NewGuid(), Name = "restaurants:delete", Description = "Delete restaurant tenant", Resource = "restaurants", Action = "delete" },

            // Catalog & Brand Administration
            new() { Id = Guid.NewGuid(), Name = "catalog:menu_update", Description = "Catalog and Brand management", Resource = "catalog", Action = "menu_update" },
        };

        bool hasNewPermissions = false;
        foreach (Permission permission in permissions)
        {
            if (!await dbContext.Permissions.AnyAsync(p => p.Name == permission.Name, ct))
            {
                dbContext.Permissions.Add(permission);
                hasNewPermissions = true;
            }
        }

        if (hasNewPermissions)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public static async Task SeedRolePermissionsAsync(IdentityDbContext dbContext, RoleManager<ApplicationRole> roleManager, CancellationToken ct = default)
    {
        List<string> allPermissionNames = await dbContext.Permissions.Select(p => p.Name).ToListAsync(ct);

        Dictionary<string, List<string>> rolePermissionMap = new()
        {
            ["SuperAdmin"] = allPermissionNames,
            ["RestaurantAdmin"] = new()
            {
                "users:view_all", "users:create", "users:edit", "users:assign_roles", "users:assign_restaurants",
                "roles:view",
                "permissions:view",
                "restaurants:edit",
                "orders:view_all", "orders:modify_confirmed", "orders:modify_ready", "orders:admin",
                "orders:write", "orders:cancel", "orders:request_cancel", "orders:approve_cancel",
                "menu:view", "menu:toggle", "menu:edit",
                "kitchen:view_orders",
                "reservations:view", "reservations:create", "reservations:edit",
                "payments:view_reports",
                "audit:view",
                "dashboard:view",
            },
            ["Manager"] = new()
            {
                "users:view_all",
                "roles:view",
                "permissions:view",
                "orders:view_all", "orders:modify_confirmed",
                "orders:write", "orders:cancel", "orders:request_cancel", "orders:approve_cancel",
                "menu:view", "menu:toggle", "menu:edit",
                "kitchen:view_orders",
                "reservations:view", "reservations:edit",
                "payments:view_reports",
                "audit:view",
                "dashboard:view",
            },
            ["KitchenManager"] = new()
            {
                "orders:view_all", "orders:modify_ordering", "orders:modify_confirmed",
                "orders:cancel", "orders:approve_cancel",
                "menu:view", "menu:toggle",
                "kitchen:view_orders", "kitchen:update_prep_status",
                "dashboard:view",
            },
            ["Waiter"] = new()
            {
                "orders:create", "orders:view_own", "orders:modify_ordering",
                "orders:write", "orders:cancel", "orders:request_cancel",
                "menu:view",
                "reservations:view", "reservations:create",
                "dashboard:view",
            },
            ["KitchenStaff"] = new()
            {
                "orders:view_all",
                "orders:cancel", "orders:approve_cancel",
                "menu:view",
                "kitchen:view_orders", "kitchen:update_prep_status",
            },
            ["Host"] = new()
            {
                "reservations:view", "reservations:create", "reservations:edit",
                "orders:view_all", "orders:request_cancel",
                "menu:view",
                "dashboard:view",
            },
            ["Cashier"] = new()
            {
                "orders:view_all",
                "orders:write", "orders:cancel", "orders:request_cancel",
                "payments:process", "payments:split_bill",
                "menu:view",
                "dashboard:view",
            },
        };

        bool hasNewRolePermissions = false;
        foreach (var (roleName, permissionNames) in rolePermissionMap)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            List<Permission> permissions = await dbContext.Permissions
                .Where(p => permissionNames.Contains(p.Name))
                .ToListAsync(ct);

            foreach (Permission permission in permissions)
            {
                if (!await dbContext.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id, ct))
                {
                    dbContext.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        Role = role,
                        PermissionId = permission.Id,
                        Permission = permission,
                    });
                    hasNewRolePermissions = true;
                }
            }
        }

        if (hasNewRolePermissions)
        {
            await dbContext.SaveChangesAsync(ct);
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
        {
            return;
        }

        string? adminEmail = "admin@orderly.com";
        ApplicationUser? existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is not null)
        {
            return;
        }

        ApplicationUser? admin = new ApplicationUser
        {
            Id = Guid.Parse("2a8c3d7e-9f14-4b5a-8c62-1d3e5f7a9b0c"),
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Sofía",
            LastName = "Mendoza",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
        };

        IdentityResult? result = await userManager.CreateAsync(admin, "Admin@123456");
        if (result.Succeeded)
        {
            ApplicationRole? role = await roleManager.FindByNameAsync("SuperAdmin");
            if (role is not null)
            {
                await userManager.AddToRoleAsync(admin, "SuperAdmin");
            }
        }
    }

    private static async Task SeedAcmeUsersAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IdentityDbContext dbContext,
        IWebHostEnvironment environment,
        CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var users = new[]
        {
            // RestaurantAdmin - Carlos Valenzuela
            new { Id = Guid.Parse("3b9d4e8f-0a25-4c6b-9d73-2e4f6a8b0c1d"), Email = "carlos.valenzuela@acme.com", FirstName = "Carlos", LastName = "Valenzuela", Role = "RestaurantAdmin" },
            new { Id = Guid.NewGuid(), Email = "admin@acme.com", FirstName = "Carlos", LastName = "Valenzuela", Role = "RestaurantAdmin" },

            // Manager - Valeria Treviño
            new { Id = Guid.Parse("4c0e5f9a-1b36-4d7c-0e84-3f5a7b9c1d2e"), Email = "valeria.trevino@acme.com", FirstName = "Valeria", LastName = "Treviño", Role = "Manager" },
            new { Id = Guid.NewGuid(), Email = "manager@acme.com", FirstName = "Valeria", LastName = "Treviño", Role = "Manager" },

            // KitchenManager - Chef Mateo Benítez
            new { Id = Guid.Parse("5d1f6a0b-2c47-4e8d-1f95-4a6b8c0d2e3f"), Email = "mateo.benitez@acme.com", FirstName = "Mateo", LastName = "Benítez", Role = "KitchenManager" },
            new { Id = Guid.NewGuid(), Email = "kitchen@acme.com", FirstName = "Mateo", LastName = "Benítez", Role = "KitchenManager" },

            // Waiter - Diego Garza
            new { Id = Guid.Parse("6e2a7b1c-3d58-4f9e-2a06-5b7c9d1e3f4a"), Email = "diego.garza@acme.com", FirstName = "Diego", LastName = "Garza", Role = "Waiter" },
            new { Id = Guid.NewGuid(), Email = "waiter@acme.com", FirstName = "Diego", LastName = "Garza", Role = "Waiter" },

            // KitchenStaff - Rodrigo Morales
            new { Id = Guid.Parse("7f3b8c2d-4e69-4a0f-3b17-6c8d0e2f4a5b"), Email = "rodrigo.morales@acme.com", FirstName = "Rodrigo", LastName = "Morales", Role = "KitchenStaff" },
            new { Id = Guid.NewGuid(), Email = "staff@acme.com", FirstName = "Rodrigo", LastName = "Morales", Role = "KitchenStaff" },

            // Host - Camila Herrera
            new { Id = Guid.Parse("8a4c9d3e-5f70-4b1a-4c28-7d9e1f3a5b6c"), Email = "camila.herrera@acme.com", FirstName = "Camila", LastName = "Herrera", Role = "Host" },
            new { Id = Guid.NewGuid(), Email = "host@acme.com", FirstName = "Camila", LastName = "Herrera", Role = "Host" },

            // Cashier - Mariana Salinas
            new { Id = Guid.Parse("9b5d0e4f-6a81-4c2b-5d39-8e0f2a4b6c7d"), Email = "mariana.salinas@acme.com", FirstName = "Mariana", LastName = "Salinas", Role = "Cashier" },
            new { Id = Guid.NewGuid(), Email = "cashier@acme.com", FirstName = "Mariana", LastName = "Salinas", Role = "Cashier" },
        };

        Guid romaNorteId = Guid.Parse("e8d2a1b5-9c4f-4e7a-8f1d-2b3c4d5e6a7b");
        Guid sanPedroId = Guid.Parse("c5b1e9a4-3f7d-4a8e-9c2b-6d7e8f1a2b3c");
        Guid legacyRestaurantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Guid[] assignedRestaurants = [romaNorteId, sanPedroId, legacyRestaurantId];

        foreach (var u in users)
        {
            ApplicationUser? user = await userManager.FindByEmailAsync(u.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = u.Id,
                    UserName = u.Email,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                    EmailConfirmed = true,
                };

                IdentityResult? result = await userManager.CreateAsync(user, "Password123!");
                if (result.Succeeded && await roleManager.RoleExistsAsync(u.Role))
                {
                    await userManager.AddToRoleAsync(user, u.Role);
                }
            }

            if (user is not null)
            {
                foreach (Guid restaurantId in assignedRestaurants)
                {
                    if (!await dbContext.UserRestaurants.AnyAsync(ur => ur.UserId == user.Id && ur.RestaurantId == restaurantId, ct))
                    {
                        dbContext.UserRestaurants.Add(new Models.UserRestaurant
                        {
                            UserId = user.Id,
                            User = user,
                            RestaurantId = restaurantId,
                            IsDefault = (restaurantId == romaNorteId),
                        });
                    }
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
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
        {
            return;
        }

        IList<ApplicationUser>? superAdmins = await userManager.GetUsersInRoleAsync("SuperAdmin");
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
        (string Name, string DisplayName, string Description)[]? customScopes = new[]
        {
            (Name: "scp:offline_access", DisplayName: "Offline access", Description: "Allows the client to request refresh tokens for offline access."),
            (Name: "scp:restaurantId", DisplayName: "Restaurant ID", Description: "Restaurant (tenant) identifier associated with the authenticated user."),
            (Name: "scp:internal", DisplayName: "Internal M2M", Description: "Machine-to-machine scope for service-bus clients (bus-style service communication)."),
        };

        foreach (var (name, displayName, description) in customScopes)
        {
            if (await scopeManager.FindByNameAsync(name) is not null)
            {
                continue;
            }

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
        {
            return;
        }

        string? redirectUri = configuration["Spa:RedirectUri"]
            ?? throw new InvalidOperationException("Spa:RedirectUri is required to seed the orderly-spa OpenIddict client.");
        string? postLogoutRedirectUri = configuration["Spa:PostLogoutRedirectUri"]
            ?? throw new InvalidOperationException("Spa:PostLogoutRedirectUri is required to seed the orderly-spa OpenIddict client.");

        OpenIddictApplicationDescriptor? descriptor = new OpenIddictApplicationDescriptor
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
        string? clientId = configuration["M2M:ClientId"]
            ?? throw new InvalidOperationException("M2M:ClientId is required to seed the M2M OpenIddict client.");

        if (await applicationManager.FindByClientIdAsync(clientId) is not null)
        {
            return;
        }

        string? clientSecret = configuration["M2M:ClientSecret"]
            ?? throw new InvalidOperationException("M2M:ClientSecret is required to seed the M2M OpenIddict client.");

        OpenIddictApplicationDescriptor? descriptor = new OpenIddictApplicationDescriptor
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

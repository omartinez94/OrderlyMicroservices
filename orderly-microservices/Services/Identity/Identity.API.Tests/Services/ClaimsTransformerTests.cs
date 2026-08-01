using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Services;

/// <summary>
/// Covers every branch of <see cref="ClaimsTransformer.GenerateClaimsAsync"/>: the
/// principal-identification branches, the role/permission joins, and the
/// default-restaurant selection rule. Downstream token issuance, authorization
/// handlers, and the Carter endpoint guards all depend on the exact claim shape
/// produced here — every regression directly affects who-can-do-what.
/// </summary>
public sealed class ClaimsTransformerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ClaimsTransformer _sut;

    public ClaimsTransformerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _userManager = TestUserManagerFactory.Create(_dbContext);
        _roleManager = TestRoleManagerFactory.Create(_dbContext);
        _sut = new ClaimsTransformer(_dbContext);
    }

    private static ClaimsPrincipal PrincipalWithUserId(Guid userId)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal PrincipalWithRawNameIdentifier(string raw)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, raw));
        return new ClaimsPrincipal(identity);
    }

    private async Task<ApplicationUser> SeedUserAsync(string email = "jane@test.com")
    {
        var user = IdentityTestData.NewUser(email);
        var result = await _userManager.CreateAsync(user, "P@ssword1!");
        result.Succeeded.Should().BeTrue();
        return user;
    }

    // -------- Identification branches --------

    /// <summary>
    /// No <c>NameIdentifier</c> claim → empty claim array. Returning empty (rather
    /// than throwing) keeps the transformer compatible with anonymous principals,
    /// which OpenIddict sometimes hands us during token refresh flows.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithoutNameIdentifier_ReturnsEmpty()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var claims = await _sut.GenerateClaimsAsync(anonymous, CancellationToken.None);

        claims.Should().BeEmpty();
    }

    /// <summary>
    /// Unparsable <c>NameIdentifier</c> → empty claim array. We must not coerce
    /// arbitrary strings to a default Guid; that would silently grant access to a
    /// different user.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithNonGuidNameIdentifier_ReturnsEmpty()
    {
        var principal = PrincipalWithRawNameIdentifier("not-a-guid");

        var claims = await _sut.GenerateClaimsAsync(principal, CancellationToken.None);

        claims.Should().BeEmpty();
    }

    /// <summary>
    /// <c>NameIdentifier</c> parses but the user no longer exists (deleted between
    /// token issuance and refresh) → empty claim array. The token should fail
    /// validation downstream rather than succeed with no role claims.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithDeletedUser_ReturnsEmpty()
    {
        var orphanId = Guid.NewGuid();
        var principal = PrincipalWithUserId(orphanId);

        var claims = await _sut.GenerateClaimsAsync(principal, CancellationToken.None);

        claims.Should().BeEmpty();
    }

    // -------- Base claims --------

    /// <summary>
    /// Happy path with a user that has no roles, no restaurants, and no
    /// permissions. The transformer must still emit the six "base" identity
    /// claims — without these, the principal would be effectively anonymous in
    /// token-validation and audit-log queries downstream.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithBareUser_EmitsBaseClaimsOnly()
    {
        var user = await SeedUserAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Select(c => c.Type).Should().BeEquivalentTo(new[]
        {
            ClaimTypes.NameIdentifier,
            ClaimTypes.Email,
            ClaimTypes.Name,
            "firstName",
            "lastName",
            "isActive",
        });
        claims.Should().NotContain(c => c.Type == ClaimTypes.Role);
        claims.Should().NotContain(c => c.Type == "restaurantId");
        claims.Should().NotContain(c => c.Type == "permissions");
    }

    /// <summary>
    /// The six base claims must carry the values the audit-log and
    /// profile-display handlers expect — not nulls, not empty strings. A null
    /// <c>Email</c> on the user record would silently coerce to empty here, so
    /// we lock in the fallback.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_BaseClaims_CarryUserValues()
    {
        var user = await SeedUserAsync(email: "ada@test.com");

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Single(c => c.Type == ClaimTypes.Email).Value.Should().Be("ada@test.com");
        claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value.Should().Be(user.Id.ToString());
        claims.Single(c => c.Type == ClaimTypes.Name).Value.Should().Be("Jane Doe");
        claims.Single(c => c.Type == "firstName").Value.Should().Be("Jane");
        claims.Single(c => c.Type == "lastName").Value.Should().Be("Doe");
        claims.Single(c => c.Type == "isActive").Value.Should().Be("True");
    }

    // -------- Roles --------

    /// <summary>
    /// Each role on the user must surface as a separate <c>ClaimTypes.Role</c>
    /// claim. ASP.NET's authorization model relies on multi-valued role claims —
    /// collapsing them into a single comma-joined claim would break role checks.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithMultipleRoles_EmitsOneClaimPerRole()
    {
        var user = await SeedUserAsync();
        // AddToRolesAsync normalizes and looks up via the store, so the roles must
        // exist in the role store first.
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Manager"));
        await _roleManager.CreateAsync(IdentityTestData.NewRole("Waiter"));
        await _userManager.AddToRolesAsync(user, new[] { "Manager", "Waiter" });

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .Should().BeEquivalentTo(new[] { "Manager", "Waiter" });
    }

    // -------- Restaurant selection --------

    /// <summary>
    /// When the user has multiple restaurant assignments, the one flagged
    /// <c>IsDefault = true</c> wins. Defaulting to <c>FirstOrDefault</c> instead
    /// would silently let an admin change which restaurant a user acts on by
    /// reordering seed inserts.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithMultipleRestaurants_PicksIsDefault()
    {
        var user = await SeedUserAsync();
        var pickMe = new Guid("11111111-2222-3333-4444-555555555555");
        _dbContext.UserRestaurants.AddRange(
            IdentityTestData.NewUserRestaurant(user, restaurantId: Guid.NewGuid(), isDefault: false),
            IdentityTestData.NewUserRestaurant(user, restaurantId: pickMe, isDefault: true),
            IdentityTestData.NewUserRestaurant(user, restaurantId: Guid.NewGuid(), isDefault: false));
        await _dbContext.SaveChangesAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Single(c => c.Type == "restaurantId").Value.Should().Be(pickMe.ToString());
    }

    /// <summary>
    /// When no restaurant is flagged <c>IsDefault</c>, the transformer falls back
    /// to the first assignment. This is a deliberate degradation: a user assigned
    /// to exactly one restaurant should always be able to act on it, even if the
    /// seed forgot to set <c>IsDefault</c>.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithNoIsDefault_FallsBackToFirst()
    {
        var user = await SeedUserAsync();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        _dbContext.UserRestaurants.AddRange(
            IdentityTestData.NewUserRestaurant(user, restaurantId: r1, isDefault: false),
            IdentityTestData.NewUserRestaurant(user, restaurantId: r2, isDefault: false));
        await _dbContext.SaveChangesAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        var values = claims.Where(c => c.Type == "restaurantId").Select(c => c.Value).ToList();
        values.Should().HaveCount(1);
        values[0].Should().BeOneOf(r1.ToString(), r2.ToString());
    }

    /// <summary>
    /// Phase 5 exit criteria: the <c>restaurantId</c> claim value MUST
    /// parse as a <see cref="Guid"/>. Pre-Phase 5 the column was
    /// <c>int</c> and the claim was emitted as <c>"42"</c> —
    /// <c>Guid.TryParse("42", out _)</c> returns <c>false</c>, so
    /// every consumer's tenant filter silently fell through to
    /// <c>Guid.Empty</c> and matched no rows. With the int→Guid
    /// migration, the claim value is now a Guid-shaped string and
    /// every downstream <c>Guid.TryParse</c> succeeds. This test
    /// pins that contract.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_RestaurantClaim_ParsesAsGuid()
    {
        var user = await SeedUserAsync();
        _dbContext.UserRestaurants.Add(
            IdentityTestData.NewUserRestaurant(user, restaurantId: Guid.NewGuid(), isDefault: true));
        await _dbContext.SaveChangesAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        var claim = claims.Single(c => c.Type == "restaurantId").Value;
        Guid.TryParse(claim, out var rid).Should().BeTrue(
            "the restaurantId claim must be a Guid-shaped string so every consumer's Guid.TryParse succeeds");
        rid.Should().NotBe(Guid.Empty,
            "the claim should never be the empty Guid — that would be the silent-fail default that triggered Phase 5");
    }

    /// <summary>
    /// A user with no restaurant assignments must not receive a <c>restaurantId</c>
    /// claim at all. Emitting an empty string would later parse to Guid.Empty and
    /// cause every scoped query in the API to silently hit the wrong tenant.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithNoRestaurants_OmitsRestaurantClaim()
    {
        var user = await SeedUserAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Should().NotContain(c => c.Type == "restaurantId");
    }

    // -------- Permissions --------

    /// <summary>
    /// Permissions must surface as one <c>"permissions"</c> claim per name, not as
    /// a single multi-value claim. The Carter endpoints use
    /// <c>principal.HasClaim("permissions", "users:view_all")</c> — that lookup
    /// only works on per-name claims.
    /// </summary>
    [Fact]
    public async Task GenerateClaimsAsync_WithPermissions_EmitsOneClaimPerPermission()
    {
        var user = await SeedUserAsync();
        var role = IdentityTestData.NewRole("Manager");
        await _roleManager.CreateAsync(role);
        await _userManager.AddToRoleAsync(user, "Manager");

        var p1 = IdentityTestData.NewPermission("users:view_all");
        var p2 = IdentityTestData.NewPermission("orders:create");
        _dbContext.Permissions.AddRange(p1, p2);
        _dbContext.RolePermissions.AddRange(
            IdentityTestData.NewRolePermission(role, p1),
            IdentityTestData.NewRolePermission(role, p2));
        await _dbContext.SaveChangesAsync();

        var claims = await _sut.GenerateClaimsAsync(
            PrincipalWithUserId(user.Id), CancellationToken.None);

        claims.Where(c => c.Type == "permissions")
            .Select(c => c.Value)
            .Should().BeEquivalentTo(new[] { "users:view_all", "orders:create" });
    }
}
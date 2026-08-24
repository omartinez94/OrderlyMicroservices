using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.API.Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>/auth/*</c>. Exercises the HTTP
/// pipeline, the real Postgres-backed <c>IdentityDbContext</c>, and
/// the production <c>UserManager&lt;ApplicationUser&gt;</c>
/// password-hashing path.
/// </summary>
/// <remarks>
/// <para>Three classes of test cover the auth surface here:</para>
/// <list type="number">
/// <item><b>Register</b> — happy path + duplicate-email guard +
/// weak-password validation. All three exercise the production
/// <c>RegisterCommandHandler</c> through the real HTTP pipeline.</item>
/// <item><b>Login</b> — bad-password path returns 401. The
/// happy-path <c>/auth/login</c> endpoint invokes
/// <c>Results.SignIn</c> outside an OpenIddict endpoint and surfaces
/// as a 500 from <c>OpenIddictServerHandlers.ValidateSignInDemand</c>
/// (test error <c>ID2091</c>) — a known production quirk this plan
/// flags but does not fix (separate work item).</item>
/// <item><b>Logout</b> — authenticated principal → 204. The
/// endpoint is gated by <c>.RequireAuthorization()</c> so anonymous
/// callers are 401'd; the handler itself short-circuits to
/// <c>Results.NoContent()</c> on a missing identity, but that
/// branch is unreachable from the HTTP pipeline.</item>
/// </list>
/// </remarks>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class AuthFlowIntegrationTests(IdentityWebApplicationFactory factory)
{
    // Identity.API configures JsonOptions.PropertyNamingPolicy = null, so the
    // server expects PascalCase. The default System.Text.Json behaviour
    // matches that — we deliberately do NOT use JsonSerializerDefaults.Web
    // (which would camelCase the test payloads and fail body binding).
    private static readonly JsonSerializerOptions JsonOptions = new();

    private HttpClient AnonymousClient() => factory.CreateClient();

    private static string FreshEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@test.com";

    // -------- Register --------

    /// <summary>
    /// Happy path: a brand-new email lands in the database via the real
    /// <c>UserManager</c> + <c>IdentityDbContext</c>; the endpoint returns
    /// 201 with a Location header pointing at <c>/users/{id}</c>.
    /// </summary>
    [Fact]
    public async Task Register_ValidPayload_Returns201AndUserCreated()
    {
        var client = AnonymousClient();
        var request = new
        {
            Email = FreshEmail("register-ok"),
            Password = "P@ssword1!",
            FirstName = "Register",
            LastName = "Ok",
        };

        var response = await client.PostAsJsonAsync("/v1/auth/register", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/v1/users/");

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Email.Should().Be(request.Email);
        body.FirstName.Should().Be("Register");
        body.LastName.Should().Be("Ok");
        body.UserId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Duplicate email → 400. The handler throws
    /// <c>BadRequestException("User with this email already exists.")</c>
    /// which the global <c>CustomExceptionHandler</c> surfaces as a 400
    /// problem details response (Phase 2 fix — added the missing
    /// <c>BadRequestException</c> arm to the handler switch).
    /// </summary>
    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var email = FreshEmail("dup");
        await factory.SeedUserAsync(email);

        var client = AnonymousClient();
        var request = new
        {
            Email = email,
            Password = "P@ssword1!",
            FirstName = "Dup",
            LastName = "User",
        };

        var response = await client.PostAsJsonAsync("/v1/auth/register", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Weak password → 400. The handler aggregates every Identity
    /// error description into a single <c>BadRequestException</c>
    /// message; the global handler translates it to a 400 problem
    /// details response. Locks in the production password policy
    /// (length ≥ 8, mixed case, digit, non-alphanumeric) is reachable
    /// through the HTTP pipeline.
    /// </summary>
    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var client = AnonymousClient();
        var request = new
        {
            Email = FreshEmail("weak-pwd"),
            Password = "weak", // length + digit + non-alnum + case all fail
            FirstName = "Weak",
            LastName = "Pwd",
        };

        var response = await client.PostAsJsonAsync("/v1/auth/register", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------- Login --------

    /// <summary>
    /// Login with a wrong password returns 401. The handler logs the
    /// failure to <c>LoginAuditLogs</c> via <c>AuditLogger</c> and
    /// returns <c>Results.Unauthorized()</c> before reaching
    /// <c>Results.SignIn</c> — so this path is testable even though
    /// the happy-path login is not.
    /// </summary>
    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var email = FreshEmail("login-bad");
        await factory.SeedUserAsync(email);

        var client = AnonymousClient();
        var request = new { Email = email, Password = "WrongP@ss1!" };

        var response = await client.PostAsJsonAsync("/v1/auth/login", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Login with an unknown email returns 401. The handler's first
    /// check (<c>userManager.FindByEmailAsync(...).IsActive</c>)
    /// short-circuits to <c>Results.Unauthorized()</c> for unknown
    /// addresses; locks in the no-user-enumeration guard at the
    /// HTTP boundary.
    /// </summary>
    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var client = AnonymousClient();
        var request = new { Email = FreshEmail("login-unknown"), Password = "AnyPassword1!" };

        var response = await client.PostAsJsonAsync("/v1/auth/login", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------- Logout --------

    /// <summary>
    /// Logout with an authenticated principal returns 204. The
    /// <c>X-Test-User</c> header injects a ClaimsPrincipal via
    /// <c>TestAuthHandler</c> so the endpoint's
    /// <c>.RequireAuthorization()</c> gate admits the call and the
    /// handler's <c>principal.Identity?.IsAuthenticated</c> branch
    /// fires. The handler writes a <c>Logout</c> row to
    /// <c>LoginAuditLogs</c> referencing the principal's user id, so
    /// the user must exist in <c>AspNetUsers</c> for the FK to
    /// accept the insert — <see cref="SeedHelper.SeedUserAsync"/>
    /// returns the seeded id so we round-trip it through the header.
    /// </summary>
    [Fact]
    public async Task Logout_Authenticated_Returns204()
    {
        var (userId, _) = await factory.SeedUserAsync(FreshEmail("logout"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", userId.ToString());

        var response = await client.PostAsync("/v1/auth/logout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Identity.API.Tests.Integration;

/// <summary>
/// HTTP pipeline coverage for <c>/audit-log/*</c>. The endpoint is
/// gated on <c>audit:view</c>; the test client injects the permission
/// via <c>TestAuthHandler</c>. Audit-log rows are seeded directly via
/// <see cref="SeedHelper.SeedAuditLogAsync"/> so the query handler has
/// deterministic input.
/// </summary>
[Collection(nameof(IdentityWebApplicationFactoryCollection))]
public sealed class AuditLogEndpointIntegrationTests(IdentityWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private HttpClient AuthenticatedClient(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Test-Permissions", string.Join(",", permissions));
        }
        return client;
    }

    // -------- List --------

    /// <summary>
    /// <c>GET /audit-log</c> with <c>audit:view</c> returns 200
    /// with the seeded audit entries. Verifies the production
    /// <c>GetAuditLogQueryHandler</c> LINQ projection (joining on
    /// <c>Users</c> for the username) hits a real Postgres column.
    /// </summary>
    [Fact]
    public async Task GetAuditLogs_Returns200()
    {
        await factory.SeedAuditLogAsync(eventType: "LoginSuccess");
        var client = AuthenticatedClient("audit:view");

        var response = await client.GetAsync("/v1/audit-log");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetAuditLogResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Logs.Should().NotBeEmpty();
        body.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // -------- Filtered list --------

    /// <summary>
    /// <c>GET /audit-log?EventType=...</c> returns only matching
    /// rows. We seed two distinct event types and filter on one to
    /// verify the <c>WHERE l.EventType == ...</c> clause in
    /// <c>GetAuditLogQueryHandler</c> is applied through the HTTP
    /// pipeline.
    /// </summary>
    [Fact]
    public async Task GetAuditLogs_WithFilters_Returns200()
    {
        await factory.SeedAuditLogAsync(eventType: "LoginSuccess");
        await factory.SeedAuditLogAsync(eventType: "LoginFailure");

        var client = AuthenticatedClient("audit:view");

        var response = await client.GetAsync("/audit-log?EventType=LoginSuccess");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetAuditLogResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Logs.Should().OnlyContain(l => l.EventType == "LoginSuccess");
    }
}

/// <summary>Response shape mirror of <c>Identity.API.Features.AuditLog.GetAuditLog.GetAuditLogResponse</c>.</summary>
internal record GetAuditLogResponse(IEnumerable<AuditLogEntry> Logs, int TotalCount);

internal record AuditLogEntry(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string EventType,
    string IpAddress,
    string UserAgent,
    DateTimeOffset Timestamp,
    string? Details);
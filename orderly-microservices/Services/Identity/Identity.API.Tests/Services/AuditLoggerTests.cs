using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Services;

/// <summary>
/// Covers every observable branch of <see cref="AuditLogger"/>: default values, null
/// tolerance, and row-isolation between calls. The logger is the single entry point
/// for compliance-sensitive audit data, so any regression here directly affects the
/// trail an investigator can reconstruct after a security incident.
/// </summary>
public sealed class AuditLoggerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _sut = new AuditLogger(_dbContext);
    }

    /// <summary>
    /// Happy path: every supplied argument is persisted verbatim to the
    /// <c>LoginAuditLogs</c> table. Locks in the field-by-field contract that the
    /// audit-log query handler reads back when displaying the trail.
    /// </summary>
    [Fact]
    public async Task LogAsync_WithAllArgs_PersistsEveryField()
    {
        var userId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        await _sut.LogAsync(
            userId,
            "LoginSuccess",
            ipAddress: "10.0.0.1",
            userAgent: "Mozilla/5.0",
            details: "ok");

        var row = _dbContext.LoginAuditLogs.Single();
        row.UserId.Should().Be(userId);
        row.EventType.Should().Be("LoginSuccess");
        row.IpAddress.Should().Be("10.0.0.1");
        row.UserAgent.Should().Be("Mozilla/5.0");
        row.Details.Should().Be("ok");
        row.Timestamp.Should().BeOnOrAfter(before);
        row.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        row.Id.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// Null <c>userId</c> is allowed. Anonymous events (failed logins for unknown
    /// emails, token issuance failures) carry no user id; rejecting them would lose
    /// forensic value on the very failures security cares about most.
    /// </summary>
    [Fact]
    public async Task LogAsync_WithNullUserId_PersistsNullUserId()
    {
        await _sut.LogAsync(null, "LoginFailure", "10.0.0.1", "Mozilla/5.0");

        var row = _dbContext.LoginAuditLogs.Single();
        row.UserId.Should().BeNull();
        row.EventType.Should().Be("LoginFailure");
    }

    /// <summary>
    /// <c>details</c> defaults to null. The handler call site may legitimately have
    /// nothing meaningful to record (e.g. a successful login with no extra context) —
    /// the field must round-trip as a real null, not an empty string.
    /// </summary>
    [Fact]
    public async Task LogAsync_WithoutDetails_StoresNull()
    {
        await _sut.LogAsync(Guid.NewGuid(), "LoginSuccess", "10.0.0.1", "Mozilla/5.0");

        _dbContext.LoginAuditLogs.Single().Details.Should().BeNull();
    }

    /// <summary>
    /// Empty <c>ipAddress</c> and <c>userAgent</c> are persisted as-is. The
    /// production modules default to the literal string <c>"unknown"</c> /
    /// <c>"N/A"</c> at the call site, but the logger itself does not — so this
    /// test pins that contract so future callers can't accidentally coerce values
    /// behind the logger's back. (Note: null <c>ipAddress</c> cannot be tested
    /// here because <c>LoginAuditLog.IpAddress</c> is a <c>required</c> non-null
    /// property — the in-memory store rejects null on save. That null-guard
    /// lives on the model itself, not the logger.)
    /// </summary>
    [Fact]
    public async Task LogAsync_WithEmptyCallerMetadata_PersistsAsIs()
    {
        await _sut.LogAsync(Guid.NewGuid(), "LoginFailure", string.Empty, string.Empty);

        var row = _dbContext.LoginAuditLogs.Single();
        row.IpAddress.Should().Be(string.Empty);
        row.UserAgent.Should().Be(string.Empty);
    }

    /// <summary>
    /// Each <c>LogAsync</c> call writes exactly one row. Login flows sometimes
    /// emit back-to-back <c>LoginSuccess</c> + <c>TokenIssued</c> events; if the
    /// logger silently batched or deduped, the audit trail would underreport.
    /// </summary>
    [Fact]
    public async Task LogAsync_CalledTwice_WritesTwoRows()
    {
        await _sut.LogAsync(Guid.NewGuid(), "LoginSuccess", "10.0.0.1", "ua");
        await _sut.LogAsync(Guid.NewGuid(), "Logout", "10.0.0.1", "ua");

        _dbContext.LoginAuditLogs.Should().HaveCount(2);
        _dbContext.LoginAuditLogs.Select(l => l.EventType)
            .Should().BeEquivalentTo(new[] { "LoginSuccess", "Logout" });
    }
}
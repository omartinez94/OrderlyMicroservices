using Identity.API.Features.AuditLog.GetAuditLog;
using Identity.API.Tests.Abstractions;

namespace Identity.API.Tests.Features.AuditLog.GetAuditLog;

/// <summary>
/// Covers every observable branch of <see cref="GetAuditLogQueryHandler"/>:
/// the two filter dimensions (UserId, EventType), the ordering, the
/// pagination math, and the <c>TotalCount</c> invariant. The handler is the
/// compliance review surface — every regression here affects what an
/// investigator can reconstruct after a security incident.
/// </summary>
public sealed class GetAuditLogQueryHandlerTests
{
    private readonly IdentityDbContext _dbContext;
    private readonly GetAuditLogQueryHandler _sut;

    public GetAuditLogQueryHandlerTests()
    {
        _dbContext = TestDbContextFactory.Create();
        _sut = new GetAuditLogQueryHandler(_dbContext);
    }

    private async Task SeedAuditLogsAsync(params LoginAuditLog[] logs)
    {
        _dbContext.LoginAuditLogs.AddRange(logs);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Default paging (page=1, pageSize=50) returns every row, ordered
    /// most-recent first. Locks in the default view of the audit-log page.
    /// </summary>
    [Fact]
    public async Task Handle_Defaults_ReturnsAllOrderedByTimestampDescending()
    {
        var userId = Guid.NewGuid();
        var older = IdentityTestData.NewAuditLog(
            userId, "LoginSuccess", timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = IdentityTestData.NewAuditLog(
            userId, "Logout", timestamp: DateTimeOffset.UtcNow);

        await SeedAuditLogsAsync(older, newer);

        var response = await _sut.Handle(new GetAuditLogQuery(), CancellationToken.None);

        response.TotalCount.Should().Be(2);
        response.Logs.Select(l => l.EventType)
            .Should().ContainInOrder("Logout", "LoginSuccess");
    }

    /// <summary>
    /// UserId filter scopes to a single user's events. Without this filter,
    /// the admin view would dump every tenant's events into one list.
    /// </summary>
    [Fact]
    public async Task Handle_WithUserIdFilter_OnlyReturnsThatUsersLogs()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await SeedAuditLogsAsync(
            IdentityTestData.NewAuditLog(alice, "LoginSuccess"),
            IdentityTestData.NewAuditLog(alice, "Logout"),
            IdentityTestData.NewAuditLog(bob, "LoginSuccess"));

        var response = await _sut.Handle(new GetAuditLogQuery(UserId: alice), CancellationToken.None);

        response.TotalCount.Should().Be(2);
        response.Logs.Should().OnlyContain(l => l.UserId == alice);
    }

    /// <summary>
    /// EventType filter scopes to a single event category. Composes with
    /// UserId when both are supplied.
    /// </summary>
    [Fact]
    public async Task Handle_WithEventTypeFilter_OnlyReturnsThatEvent()
    {
        var userId = Guid.NewGuid();
        await SeedAuditLogsAsync(
            IdentityTestData.NewAuditLog(userId, "LoginSuccess"),
            IdentityTestData.NewAuditLog(userId, "LoginFailure"),
            IdentityTestData.NewAuditLog(userId, "Logout"));

        var onlyFailures = await _sut.Handle(new GetAuditLogQuery(EventType: "LoginFailure"), CancellationToken.None);
        onlyFailures.TotalCount.Should().Be(1);
        onlyFailures.Logs.Single().EventType.Should().Be("LoginFailure");
    }

    /// <summary>
    /// Both filters applied → AND semantics. Locks in the combined-filter
    /// contract for "show me alice's failed logins only".
    /// </summary>
    [Fact]
    public async Task Handle_WithBothFilters_AppliesAnd()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await SeedAuditLogsAsync(
            IdentityTestData.NewAuditLog(alice, "LoginSuccess"),
            IdentityTestData.NewAuditLog(alice, "LoginFailure"),
            IdentityTestData.NewAuditLog(bob, "LoginFailure"));

        var response = await _sut.Handle(
            new GetAuditLogQuery(UserId: alice, EventType: "LoginFailure"),
            CancellationToken.None);

        response.TotalCount.Should().Be(1);
        response.Logs.Single().UserId.Should().Be(alice);
        response.Logs.Single().EventType.Should().Be("LoginFailure");
    }

    /// <summary>
    /// Anonymous events (UserId is null) are still returned when no UserId
    /// filter is applied. The UserName projection falls back to null in this
    /// case, which is the audit-viewer's signal that the event happened
    /// against a non-existent or unauthenticated principal.
    /// </summary>
    [Fact]
    public async Task Handle_WithAnonymousEvent_ProjectsNullUserName()
    {
        await SeedAuditLogsAsync(IdentityTestData.NewAuditLog(
            userId: null, eventType: "LoginFailure"));

        var response = await _sut.Handle(new GetAuditLogQuery(), CancellationToken.None);

        var entry = response.Logs.Single();
        entry.UserId.Should().BeNull();
        entry.UserName.Should().BeNull();
        entry.EventType.Should().Be("LoginFailure");
    }

    /// <summary>
    /// Pagination: page 2 with pageSize 1 returns the second-newest entry,
    /// and <c>TotalCount</c> still reflects the unpaginated row count of 3.
    /// </summary>
    [Fact]
    public async Task Handle_WithPagination_ReturnsCorrectSliceAndTotalCount()
    {
        var userId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-20);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-10);
        await SeedAuditLogsAsync(
            IdentityTestData.NewAuditLog(userId, "LoginSuccess", timestamp: t0),
            IdentityTestData.NewAuditLog(userId, "TokenIssued", timestamp: t1),
            IdentityTestData.NewAuditLog(userId, "Logout", timestamp: t2));

        var response = await _sut.Handle(new GetAuditLogQuery(Page: 2, PageSize: 1), CancellationToken.None);

        response.Logs.Should().HaveCount(1);
        response.Logs.Single().EventType.Should().Be("TokenIssued"); // middle in time order
        response.TotalCount.Should().Be(3);
    }
}
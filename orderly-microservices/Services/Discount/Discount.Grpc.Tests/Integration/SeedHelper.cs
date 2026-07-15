namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Factory-extension helpers for seeding fixtures in
/// <see cref="DiscountWebApplicationFactory"/> integration tests. Centralizes
/// the "open a scope, attach a coupon, save" pattern so test bodies stay
/// readable. Direct <see cref="DbContext.Add"/> bypasses the global query
/// filter for the inserted row (EF Core's <c>Add</c> path doesn't apply
/// query filters on insert — only on read); reads still respect the
/// tenant filter.
/// </summary>
public static class SeedHelper
{
    /// <summary>
    /// Inserts a coupon row directly via the production scope. Returns
    /// the saved <see cref="Coupon"/> so the test can read the assigned
    /// <see cref="Models.Coupon.Id"/> for follow-up assertions.
    /// </summary>
    public static async Task<Coupon> SeedCouponAsync(
        this DiscountWebApplicationFactory factory,
        Guid restaurantId,
        string code,
        decimal amount = 10m,
        int redeemAmount = 0,
        int? maxRedeemAmount = null,
        Instant? expirationDate = null,
        string? description = null,
        bool isActive = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();

        // Bypass the global tenant query filter for the seed insert:
        // EF Core's `Add` skips filters on insert, but we still set
        // tenant fields explicitly so the row matches the tenant its
        // caller expects. Avoids `.IgnoreQueryFilters()` round-trips.
        //
        // CreatedBy / LastModifiedBy / IsActive setters are protected on
        // AuditableEntity<T>; AuditableEntityInterceptor stamps them
        // from IHttpContextAccessor during SaveChangesAsync. Tests
        // don't have an HTTP scope, so the interceptor falls back to
        // its no-context default. We don't set the audit fields here
        // because the compile error would surface as CS0272.
        var coupon = new Coupon
        {
            RestaurantId = restaurantId,
            Code = code,
            Description = description ?? "seed",
            Amount = amount,
            RedeemAmount = redeemAmount,
            MaxRedeemAmount = maxRedeemAmount,
            ExpirationDate = expirationDate,
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();

        // AuditableEntity.IsActive has `protected set`; the
        // AuditableEntityInterceptor would set it from a real ClaimsPrincipal
        // but tests don't have an HTTP scope. Set IsActive=1 explicitly
        // via raw SQL after insert so the conditional UPDATE in
        // RedeemDiscount matches the row. The save above gives us the
        // assigned Id; the explicit UPDATE locks in the active flag.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE Coupons SET IsActive = {1} WHERE Id = {coupon.Id}
        ");

        // Refresh the entity so the caller sees IsActive=1 without a
        // round-trip. (Coupons are tracked by default; ExecuteSqlInterpolatedAsync
        // doesn't refresh the tracker.)
        await db.Entry(coupon).ReloadAsync();
        return coupon;
    }

    /// <summary>
    /// Truncates every Discount-owned table so a test can start with a
    /// clean baseline. Cascade not configured (Discount has no FKs
    /// into other tables yet), so a single <c>ExecuteDeleteAsync</c> per
    /// table is sufficient. Order matters: outbox tables first so the
    /// test's prior history-publish rows don't leak across fixtures.
    /// <c>IgnoreQueryFilters()</c> is required on the tenant-scoped
    /// tables (Coupon, RewardCode) because the global query filter
    /// scopes ExecuteDeleteAsync to the active caller's tenant — without
    /// it, prior tests' cross-tenant rows survive and pollute counts.
    /// </summary>
    public static async Task CleanAllAsync(this DiscountWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        await db.OutboxMessages.ExecuteDeleteAsync();
        await db.OutboxDeadMessages.ExecuteDeleteAsync();
        await db.RewardCodes.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.DiscountRules.ExecuteDeleteAsync();
        await db.ProcessedInboundevents.ExecuteDeleteAsync();
        await db.Coupons.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    /// <summary>
    /// Inserts a reward-code row directly via the production scope.
    /// Returns the saved <see cref="RewardCode"/> with its assigned
    /// <see cref="Models.RewardCode.Id"/>. Mirrors <see cref="SeedCouponAsync"/>'s
    /// shape: bypasses the global tenant filter for the insert (EF Core's
    /// <c>Add</c> path doesn't apply query filters on insert) and stamps
    /// <c>IsActive=1</c> via a follow-up raw UPDATE so the conditional
    /// UPDATE in <c>RedeemRewardCode</c> matches the row.
    /// </summary>
    public static async Task<RewardCode> SeedRewardCodeAsync(
        this DiscountWebApplicationFactory factory,
        Guid restaurantId,
        string code,
        RewardKind kind = RewardKind.Percentage,
        decimal value = 10m,
        string? description = null,
        Instant? expirationDate = null,
        int? maxRedeemAmount = null,
        bool isActive = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();

        var row = new RewardCode
        {
            RestaurantId = restaurantId,
            Code = code,
            Kind = kind,
            Value = value,
            Description = description,
            ExpirationDate = expirationDate,
            MaxRedeemAmount = maxRedeemAmount,
        };
        db.RewardCodes.Add(row);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE RewardCodes SET IsActive = {(isActive ? 1 : 0)} WHERE Id = {row.Id}
        ");

        await db.Entry(row).ReloadAsync();
        return row;
    }

    /// <summary>
    /// Stamps <paramref name="factory"/>'s <see cref="BrokerHealthState"/>
    /// to <paramref name="consecutiveFailures"/> for the duration of the
    /// returned disposable — restores the prior value on dispose.
    /// Drives /ready health-check and circuit-breaker tests.
    /// </summary>
    public static IDisposable TripBrokerCircuit(
        this DiscountWebApplicationFactory factory,
        int consecutiveFailures)
    {
        using var probeScope = factory.Services.CreateScope();
        var state = probeScope.ServiceProvider
            .GetRequiredService<BrokerHealthState>();

        for (var i = 0; i < consecutiveFailures; i++)
        {
            state.RecordFailure();
        }

        return new CircuitReset(state);
    }

    private sealed class CircuitReset(BrokerHealthState state) : IDisposable
    {
        public void Dispose() => state.Reset();
    }
}

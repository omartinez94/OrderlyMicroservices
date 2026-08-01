namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Drives the same atomic conditional UPDATE that
/// <see cref="Grpc.Services.DiscountService.RedeemDiscount"/> emits and
/// asserts the Postgres write-lock serializes concurrent redemptions
/// correctly. Plan §7 Phase 1 race-fix verification.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class RedeemDiscountRaceTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("dddddddd-0000-0000-0000-000000000001");

    [Fact]
    public async Task SingleRedeem_IncrementsRedeemAmount_AndReturnsSuccess()
    {
        await factory.CleanAllAsync();
        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "RACE-SINGLE",
            redeemAmount: 0,
            maxRedeemAmount: 5);

        var rowsAffected = await RunConditionalRedeemAsync(coupon.Id);
        rowsAffected.Should().Be(1, "a single redemption within cap should succeed");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var row = await db.Coupons.IgnoreQueryFilters().FirstAsync(c => c.Id == coupon.Id);
        row.RedeemAmount.Should().Be(1);
    }

    [Fact]
    public async Task FiveConcurrentRedeem_AgainstThreeCap_HasThreeWinners()
    {
        await factory.CleanAllAsync();
        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "RACE-CAP-3",
            redeemAmount: 0,
            maxRedeemAmount: 3);

        // Five parallel attempts against a cap of three. PostgreSQL
        // serializes writes via row-level locks; each
        // ExecuteSqlInterpolatedAsync opens its own implicit transaction
        // that holds a row lock until commit, so the contended write is
        // real. Expect exactly 3 to increment and 2 to fail the predicate.
        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
            RunConditionalRedeemAsync(coupon.Id))).ToArray();

        var rowsAffected = await Task.WhenAll(tasks);
        var winners = rowsAffected.Count(r => r == 1);
        var losers = rowsAffected.Count(r => r == 0);

        winners.Should().Be(3, "the cap-of-3 must allow exactly 3 increments");
        losers.Should().Be(2, "the remaining 2 attempts must lose the race");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var row = await db.Coupons.IgnoreQueryFilters().FirstAsync(c => c.Id == coupon.Id);
        row.RedeemAmount.Should().Be(3, "the increment count must match the winner count");
    }

    [Fact]
    public async Task OverCap_LosesRace()
    {
        await factory.CleanAllAsync();
        var coupon = await factory.SeedCouponAsync(
            TenantGuid,
            code: "RACE-ALREADY-FULL",
            redeemAmount: 3,
            maxRedeemAmount: 3);

        var rowsAffected = await RunConditionalRedeemAsync(coupon.Id);
        rowsAffected.Should().Be(0, "a coupon already at cap should not redeem");
    }

    /// <summary>Runs the exact atomic conditional-UPDATE pattern that
    /// <see cref="Grpc.Services.DiscountService.RedeemDiscount"/> emits.
    /// Tests deliberately skip the audit-column writes so the test
    /// focuses on the race-fix SQL behaviour, not the audit interceptor.</summary>
    private async Task<int> RunConditionalRedeemAsync(int couponId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        return await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""Coupons""
            SET ""RedeemAmount"" = ""RedeemAmount"" + 1
            WHERE ""Id"" = {couponId}
              AND ""IsActive"" = {true}
              AND ""DeletedAt"" IS NULL
              AND (""MaxRedeemAmount"" IS NULL OR ""RedeemAmount"" < ""MaxRedeemAmount"")
        ");
    }
}

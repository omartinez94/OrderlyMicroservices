namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Mirrors <see cref="RedeemDiscountRaceTests"/> for the
/// <see cref="Grpc.Services.RewardCodeService.RedeemRewardCode"/> atomic
/// conditional UPDATE. Plan §7 Phase 3 closes the same TOCTOU race for
/// reward codes that Phase 1 closed for coupons. SQLite serializes
/// writes via the engine-level write lock; concurrent redemptions against
/// a cap produce exactly <c>cap</c> winners and <c>attempts - cap</c> losers.
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class RedeemRewardCodeRaceTests(DiscountWebApplicationFactory factory)
{
    private static readonly Guid TenantGuid = new("dddddddd-0000-0000-0000-000000000002");

    [Fact]
    public async Task SingleRedeem_IncrementsRedeemAmount_AndReturnsSuccess()
    {
        await factory.CleanAllAsync();
        var row = await factory.SeedRewardCodeAsync(
            TenantGuid,
            code: "RACE-RWD-SINGLE",
            maxRedeemAmount: 5);

        var rowsAffected = await RunConditionalRedeemAsync(row.Id);
        rowsAffected.Should().Be(1, "a single redemption within cap should succeed");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.RewardCodes.IgnoreQueryFilters().FirstAsync(r => r.Id == row.Id);
        stored.RedeemAmount.Should().Be(1);
    }

    [Fact]
    public async Task FiveConcurrentRedeem_AgainstThreeCap_HasThreeWinners()
    {
        await factory.CleanAllAsync();
        var row = await factory.SeedRewardCodeAsync(
            TenantGuid,
            code: "RACE-RWD-CAP-3",
            maxRedeemAmount: 3);

        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
            RunConditionalRedeemAsync(row.Id))).ToArray();

        var rowsAffected = await Task.WhenAll(tasks);
        var winners = rowsAffected.Count(r => r == 1);
        var losers = rowsAffected.Count(r => r == 0);

        winners.Should().Be(3, "the cap-of-3 must allow exactly 3 increments");
        losers.Should().Be(2, "the remaining 2 attempts must lose the race");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        var stored = await db.RewardCodes.IgnoreQueryFilters().FirstAsync(r => r.Id == row.Id);
        stored.RedeemAmount.Should().Be(3);
    }

    [Fact]
    public async Task OverCap_LosesRace()
    {
        await factory.CleanAllAsync();
        // Pre-load at cap by inserting with RedeemAmount already at cap.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            db.RewardCodes.Add(new Discount.Grpc.Models.RewardCode
            {
                RestaurantId = TenantGuid,
                Code = "RACE-RWD-FULL",
                Kind = Discount.Grpc.Models.RewardKind.FixedAmount,
                Value = 5m,
                RedeemAmount = 3,
                MaxRedeemAmount = 3,
            });
            await db.SaveChangesAsync();
        }

        // Re-read to get Id.
        int rowId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var existing = await db.RewardCodes.IgnoreQueryFilters()
                .FirstAsync(r => r.Code == "RACE-RWD-FULL");
            rowId = existing.Id;
        }

        var rowsAffected = await RunConditionalRedeemAsync(rowId);
        rowsAffected.Should().Be(0, "a reward code already at cap should not redeem");
    }

    /// <summary>Runs the exact atomic conditional-UPDATE pattern that
    /// <see cref="Grpc.Services.RewardCodeService.RedeemRewardCode"/> emits.
    /// Tests deliberately skip the audit-column writes so the test focuses
    /// on the race-fix SQL behaviour, not the audit interceptor.</summary>
    private async Task<int> RunConditionalRedeemAsync(int rewardCodeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        return await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE RewardCodes
            SET RedeemAmount = RedeemAmount + 1
            WHERE Id = {rewardCodeId}
              AND IsActive = 1
              AND DeletedAt IS NULL
              AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)
        ");
    }
}
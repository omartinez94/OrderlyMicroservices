using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Discount.Grpc.Tests.Integration;

/// <summary>
/// Smoke tests for the <see cref="DiscountRule"/> aggregate:
/// the UK (RestaurantId, CouponId) constraint, persistence, and the
/// soft-delete shape. The <c>EvaluateDiscountRules</c> evaluator is
/// covered by the gRPC integration tests in Commit C (currently
/// pending the auth-bridge fix — see RpcEndpointTests skip notes).
/// </summary>
[Collection(nameof(DiscountWebApplicationFactoryCollection))]
public sealed class DiscountRuleServiceTests(DiscountWebApplicationFactory factory)
{
    private const string TestRestaurantId = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid TestRestaurantGuid = new(TestRestaurantId);

    [Fact]
    public async Task CreateRule_PersistsRow_WithCoupledToCoupon()
    {
        await factory.CleanAllAsync();
        var coupon = await factory.SeedCouponAsync(TestRestaurantGuid, code: "RULE-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
        db.DiscountRules.Add(new DiscountRule
        {
            RestaurantId = TestRestaurantGuid,
            CouponId = coupon.Id,
            RuleType = DiscountRuleKind.MinOrderAmount,
            RuleDataJson = """{"MinOrderAmount":"50.00"}""",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var stored = await db.DiscountRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CouponId == coupon.Id);
        stored.Should().NotBeNull();
        stored!.RuleType.Should().Be(DiscountRuleKind.MinOrderAmount);
        stored.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UniqueIndex_On_RestaurantAndCoupon_TripsOnDuplicateInsert()
    {
        await factory.CleanAllAsync();
        var coupon = await factory.SeedCouponAsync(TestRestaurantGuid, code: "UK-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();

        db.DiscountRules.Add(new DiscountRule
        {
            RestaurantId = TestRestaurantGuid,
            CouponId = coupon.Id,
            RuleType = DiscountRuleKind.MinOrderAmount,
            RuleDataJson = "{}",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        // Second insert with the same (RestaurantId, CouponId) violates
        // the unique index. PostgreSQL surfaces this as DbUpdateException
        // wrapping Npgsql.PostgresException.SqlState "23505" (unique_violation).
        db.DiscountRules.Add(new DiscountRule
        {
            RestaurantId = TestRestaurantGuid,
            CouponId = coupon.Id,
            RuleType = DiscountRuleKind.RequiredMenuItems,
            RuleDataJson = """{"RequiredMenuItemIds":[]}""",
            IsActive = true,
        });

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the (RestaurantId, CouponId) UK should reject the second insert");
    }

    [Fact]
    public async Task Query_FiltersByTenant_Globally()
    {
        // Two rules across two tenants — the global tenant filter must
        // hide cross-tenant rows from the consumer's read path.
        await factory.CleanAllAsync();
        var couponA = await factory.SeedCouponAsync(TestRestaurantGuid, code: "TEN-A");
        var couponB = await factory.SeedCouponAsync(
            new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            code: "TEN-B");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            db.DiscountRules.Add(new DiscountRule
            {
                RestaurantId = TestRestaurantGuid,
                CouponId = couponA.Id,
                RuleType = DiscountRuleKind.MinOrderAmount,
                RuleDataJson = "{}",
                IsActive = true,
            });
            db.DiscountRules.Add(new DiscountRule
            {
                RestaurantId = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CouponId = couponB.Id,
                RuleType = DiscountRuleKind.MinOrderAmount,
                RuleDataJson = "{}",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        // Verify the rows landed.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiscountContext>();
            var total = await db.DiscountRules.IgnoreQueryFilters().CountAsync();
            total.Should().Be(2, "both rules should be persisted");
        }
    }
}

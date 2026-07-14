using NodaTime;
using BuildingBlocks.Entities.Contracts;
using BuildingBlocks.Multitenancy;

namespace Discount.Grpc.Models;

public class Coupon : AuditableEntity<int>, ITenantEntity
{
    public Guid RestaurantId { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public int RedeemAmount { get; set; } = 0;
    public int? MaxRedeemAmount { get; set; }
    public Instant? ExpirationDate { get; set; }

    // Soft-delete columns — set by DiscountExpirySweepService when the coupon
    // expires. AuditableEntity.IsActive stays a separate business-flag: an admin
    // can deactivate a coupon (IsActive = false) before its expiration, and the
    // sweep soft-deletes an expired one (DeletedAt != null). Both flag-gates
    // participate in the global query filter — see DiscountContext.OnModelCreating.
    public Instant? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}


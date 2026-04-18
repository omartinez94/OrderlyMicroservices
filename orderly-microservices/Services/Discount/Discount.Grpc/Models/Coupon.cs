using NodaTime;
using BuildingBlocks.Entities.Contracts;

namespace Discount.Grpc.Models;

public class Coupon : AuditableEntity<int>
{
    public Guid RestaurantId { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public int RedeemAmount { get; set; } = 0;
    public int? MaxRedeemAmount { get; set; }
    public Instant? ExpirationDate { get; set; }
}

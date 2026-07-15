using Discount.Grpc.Authorization;
using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Discount.Grpc.Services;

public class DiscountService(ILogger<DiscountService> logger, DiscountContext dbContext)
    : DiscountProtoService.DiscountProtoServiceBase
{
    [Permission(DiscountPermissions.CouponRead)]
    public override async Task<GetDiscountResponse> GetDiscount(GetDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("GetDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);
        
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.RestaurantId == Guid.Parse(request.RestaurantId) && c.Code == request.Code);

        if (coupon is null)
        {
            // Empty coupon response to indicate no discount found for the given restaurant and code
            return new GetDiscountResponse 
            { 
                Coupon = new CouponModel 
                { 
                    RestaurantId = request.RestaurantId,
                    Code = String.Empty,
                    Description = String.Empty,
                    Amount = 0,
                    IsActive = false
                } 
            };
        }

        return new GetDiscountResponse { Coupon = ToProtoModel(coupon) };
    }

    [Permission(DiscountPermissions.CouponCreate)]
    public override async Task<CreateDiscountResponse> CreateDiscount(CreateDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("CreateDiscount called for RestaurantId: {RestaurantId}, Coupon Code: {Code}", request.Coupon.RestaurantId, request.Coupon.Code);

        if(string.IsNullOrEmpty(request.Coupon.RestaurantId) || string.IsNullOrEmpty(request.Coupon.Code))
        {
            return new CreateDiscountResponse 
            { 
                Coupon = request.Coupon,
                Success = false
            };
        }

        var coupon = ToEntity(request.Coupon);
        
        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync();

        return new CreateDiscountResponse 
        { 
            Coupon = ToProtoModel(coupon),
            Success = true
        };
    }

    [Permission(DiscountPermissions.CouponEdit)]
    public override async Task<UpdateDiscountResponse> UpdateDiscount(UpdateDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("UpdateDiscount called for RestaurantId: {RestaurantId}, Coupon Code: {Code}", request.Coupon.RestaurantId, request.Coupon.Code);
        
        var coupon = await dbContext.Coupons.FindAsync(request.Coupon.Id);
        if (coupon is null)
        {
            return new UpdateDiscountResponse 
            { 
                Coupon = request.Coupon,
                Success = false
            };
        }

        coupon.RestaurantId = Guid.Parse(request.Coupon.RestaurantId);
        coupon.Code = request.Coupon.Code;
        coupon.Description = request.Coupon.Description;
        coupon.Amount = (decimal)request.Coupon.Amount;
        coupon.MaxRedeemAmount = request.Coupon.MaxRedeemAmount == 0 ? null : request.Coupon.MaxRedeemAmount;
        
        if (!string.IsNullOrEmpty(request.Coupon.ExpirationDate))
        {
            coupon.ExpirationDate = InstantPattern.ExtendedIso.Parse(request.Coupon.ExpirationDate).Value;
        }
        else
        {
            coupon.ExpirationDate = null;
        }

        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync();

        return new UpdateDiscountResponse 
        { 
            Coupon = ToProtoModel(coupon),
            Success = true
        };
    }

    [Permission(DiscountPermissions.CouponDelete)]
    public override async Task<DeleteDiscountResponse> DeleteDiscount(DeleteDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("DeleteDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);
        
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.RestaurantId == Guid.Parse(request.RestaurantId) && c.Code == request.Code);

        if (coupon is null)
        {
            return new DeleteDiscountResponse { Success = false };
        }

        dbContext.Coupons.Remove(coupon);
        await dbContext.SaveChangesAsync();
        return new DeleteDiscountResponse { Success = true };
    }

    [Permission(DiscountPermissions.CouponRedeem)]
    public override async Task<RedeemDiscountResponse> RedeemDiscount(RedeemDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("RedeemDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);

        // First pass: resolve the coupon to its Id. The global query filter
        // (tenant + DeletedAt == null) keeps us inside the caller's tenant and
        // excludes soft-deleted rows. A second pass won't be needed — the
        // conditional UPDATE below is the atomic gate.
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.RestaurantId == Guid.Parse(request.RestaurantId) && c.Code == request.Code);

        if (coupon is null)
        {
            return new RedeemDiscountResponse { Success = false };
        }

        // Atomic conditional UPDATE. SQLite locks the row inside its implicit
        // transaction; concurrent redemptions serialize and the loser sees
        // rowsAffected = 0 instead of incrementing past MaxRedeemAmount. The
        // pre-existing TOCTOU race is closed because the read-then-write pair
        // collapses into one engine-native UPDATE. WHERE-clause guards:
        //   - alive   (DeletedAt IS NULL)        — defensive; the read already enforced this
        //   - active  (IsActive = 1)              — defensive; the global filter doesn't yet gate on IsActive
        //   - under cap (RedeemAmount < cap, OR cap unset)
        // Plan §1 row "concurrency" calls this the SQLite-correct race fix.
        //
        // Audit-column note: raw ExecuteSqlInterpolatedAsync bypasses the
        // AuditableEntityInterceptor, so we set LastModifiedAt + LastModifiedBy
        // explicitly here. The actor is `DiscountActors.System` (distinct from
        // `DiscountActors.Sweep`, which is reserved for the expiry-sweep host)
        // per plan §v1.1 L11.
        var now = SystemClock.Instance.GetCurrentInstant();
        var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE Coupons
            SET RedeemAmount    = RedeemAmount + 1,
                LastModifiedAt  = {now},
                LastModifiedBy  = {DiscountActors.System}
            WHERE Id = {coupon.Id}
              AND IsActive = 1
              AND DeletedAt IS NULL
              AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)
        ");

        if (rowsAffected == 0)
        {
            // Either (a) a concurrent redemption took the last available slot
            // just before us, or (b) an admin deactivated or soft-deleted the
            // coupon between our read and our write. Surface as Success = false;
            // the Idempotency-Key layer ensures the caller
            // can safely retry without double-redemption.
            return new RedeemDiscountResponse { Success = false };
        }

        return new RedeemDiscountResponse { Success = true };
    }

    /// <summary>
    /// Paged list of coupons for the active restaurant. The query path
    /// runs through the global tenant filter (no manual
    /// <c>Where(RestaurantId == ...)</c>) so the cross-tenant-deny default
    /// kicks in if <see cref="ICurrentRestaurantProvider"/> can't resolve a
    /// restaurant. Per plan §0.4.1 H-L16.
    /// </summary>
    /// <remarks>
    /// Pagination contract: <c>page</c> is 1-based; <c>page_size</c> is
    /// clamped server-side to <c>[1, 200]</c> with a default of 50 (an
    /// out-of-range or zero <c>page_size</c> falls back to the default).
    /// The <c>total_count</c> on the response is the count of *alive*
    /// (DeletedAt IS NULL) rows for the active tenant — matches the row
    /// set the caller can page through.
    /// </remarks>
    [Permission(DiscountPermissions.CouponRead)]
    public override async Task<ListDiscountsResponse> ListDiscounts(ListDiscountsRequest request, ServerCallContext context)
    {
        logger.LogInformation(
            "ListDiscounts called for RestaurantId: {RestaurantId}, page: {Page}, pageSize: {PageSize}.",
            request.RestaurantId, request.Page, request.PageSize);

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            <= 0 => 50,                                       // default
            > 200 => 200,                                     // plan §0.4.1 cap
            _ => request.PageSize,
        };

        // The global query filter (tenant + DeletedAt IS NULL) handles
        // tenant scoping without an explicit WHERE here. Total count
        // reflects the same filter so the paged total matches what the
        // caller can navigate to.
        var baseQuery = dbContext.Coupons.AsNoTracking();
        var totalCount = await baseQuery.CountAsync();

        var pageRows = await baseQuery
            .OrderBy(c => c.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = new ListDiscountsResponse
        {
            TotalCount = totalCount,
        };
        response.Coupons.AddRange(pageRows.Select(ToProtoModel));
        return response;
    }

    private static CouponModel ToProtoModel(Coupon coupon)
    {
        return new CouponModel
        {
            Id = coupon.Id,
            RestaurantId = coupon.RestaurantId.ToString(),
            Code = coupon.Code,
            Description = coupon.Description,
            Amount = (double)coupon.Amount,
            RedeemAmount = coupon.RedeemAmount,
            MaxRedeemAmount = coupon.MaxRedeemAmount ?? 0,
            ExpirationDate = coupon.ExpirationDate?.ToString() ?? String.Empty,
            IsActive = coupon.IsActive
        };
    }

    private static Coupon ToEntity(CouponModel model)
    {
        return new Coupon
        {
            Id = model.Id,
            RestaurantId = Guid.Parse(model.RestaurantId),
            Code = model.Code,
            Description = model.Description,
            Amount = (decimal)model.Amount,
            RedeemAmount = model.RedeemAmount,
            MaxRedeemAmount = model.MaxRedeemAmount == 0 ? null : model.MaxRedeemAmount,
            ExpirationDate = string.IsNullOrEmpty(model.ExpirationDate) ? null : InstantPattern.ExtendedIso.Parse(model.ExpirationDate).Value
        };
    }
}

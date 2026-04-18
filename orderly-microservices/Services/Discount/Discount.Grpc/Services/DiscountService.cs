using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Text;

namespace Discount.Grpc.Services;

public class DiscountService(ILogger<DiscountService> logger, DiscountContext dbContext) 
    : DiscountProtoService.DiscountProtoServiceBase
{
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
                    Amount = 0
                } 
            };
        }

        return new GetDiscountResponse { Coupon = ToProtoModel(coupon) };
    }

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

    public override async Task<RedeemDiscountResponse> RedeemDiscount(RedeemDiscountRequest request, ServerCallContext context)
    {
        logger.LogInformation("RedeemDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);
        
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.RestaurantId == Guid.Parse(request.RestaurantId) && c.Code == request.Code);

        if (coupon is null)
        {
            return new RedeemDiscountResponse { Success = false };
        }
        
        if (coupon.MaxRedeemAmount.HasValue && coupon.RedeemAmount >= coupon.MaxRedeemAmount.Value)
        {
            return new RedeemDiscountResponse { Success = false };
        }

        coupon.RedeemAmount += 1;
        
        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync();
        
        return new RedeemDiscountResponse { Success = true };
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

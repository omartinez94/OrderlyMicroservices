using Grpc.Core;

namespace Discount.Grpc.Services;

public class DiscountService : DiscountProtoService.DiscountProtoServiceBase
{
    private readonly ILogger<DiscountService> _logger;

    public DiscountService(ILogger<DiscountService> logger)
    {
        _logger = logger;
    }

    public override Task<GetDiscountResponse> GetDiscount(GetDiscountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);
        
        // TODO: Implement logic using MediatR and Marten or your DB context
        throw new RpcException(new Status(StatusCode.Unimplemented, "GetDiscount is not implemented yet."));
    }

    public override Task<CreateDiscountResponse> CreateDiscount(CreateDiscountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("CreateDiscount called for Coupon Code: {Code}", request.Coupon.Code);
        
        throw new RpcException(new Status(StatusCode.Unimplemented, "CreateDiscount is not implemented yet."));
    }

    public override Task<UpdateDiscountResponse> UpdateDiscount(UpdateDiscountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("UpdateDiscount called for Coupon Code: {Code}", request.Coupon.Code);
        
        throw new RpcException(new Status(StatusCode.Unimplemented, "UpdateDiscount is not implemented yet."));
    }

    public override Task<DeleteDiscountResponse> DeleteDiscount(DeleteDiscountRequest request, ServerCallContext context)
    {
        _logger.LogInformation("DeleteDiscount called for RestaurantId: {RestaurantId}, Code: {Code}", request.RestaurantId, request.Code);
        
        throw new RpcException(new Status(StatusCode.Unimplemented, "DeleteDiscount is not implemented yet."));
    }
}

namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedback;

public record GetCustomerFeedbackRequest(
    Guid? OrderId,
    int? MinRating,
    int? MaxRating,
    LocalDate? From,
    LocalDate? To,
    bool? RewardRedeemed,
    int PageIndex = 0,
    int PageSize = 20);

public record CustomerFeedbackDto(
    int Id,
    Guid RestaurantId,
    Guid OrderId,
    int OverallRating,
    int FoodQualityRating,
    int ServiceSpeedRating,
    int WaiterFriendlinessRating,
    string Comments,
    Instant SubmittedAt,
    string RewardType,
    decimal? RewardValue,
    string RewardDescription,
    string RewardCode,
    bool RewardRedeemed,
    Instant? RedeemedAt,
    Guid? RedeemedInOrderId);

public record GetCustomerFeedbackResponse(
    IEnumerable<CustomerFeedbackDto> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public class GetCustomerFeedbackEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("CustomerFeedback");

        group.MapGet("/restaurants/{restaurantId}/feedback", async (
            Guid restaurantId,
            [AsParameters] GetCustomerFeedbackRequest request,
            ISender sender) =>
        {
            var query = new GetCustomerFeedbackQuery(
                restaurantId,
                request.OrderId,
                request.MinRating,
                request.MaxRating,
                request.From,
                request.To,
                request.RewardRedeemed,
                request.PageIndex,
                request.PageSize);

            var result = await sender.Send(query);
            return Results.Ok(result.Adapt<GetCustomerFeedbackResponse>());
        })
        .WithDescription("Gets customer feedback entries for a restaurant.")
        .WithName("GetCustomerFeedback")
        .Produces<GetCustomerFeedbackResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
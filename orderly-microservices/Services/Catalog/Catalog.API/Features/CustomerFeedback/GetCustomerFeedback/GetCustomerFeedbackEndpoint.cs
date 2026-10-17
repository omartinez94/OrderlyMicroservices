using NodaTime.Text;

namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedback;

public record GetCustomerFeedbackRequest(
    Guid? OrderId = null,
    int? MinRating = null,
    int? MaxRating = null,
    string? From = null,
    string? To = null,
    bool? RewardRedeemed = null,
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
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("CustomerFeedback");

        group.MapGet("/restaurants/{restaurantId}/feedback", async (
            Guid restaurantId,
            [AsParameters] GetCustomerFeedbackRequest request,
            ISender sender) =>
        {
            LocalDate? from = null;
            if (!string.IsNullOrEmpty(request.From))
            {
                ParseResult<LocalDate> parseFrom = LocalDatePattern.Iso.Parse(request.From);
                if (parseFrom.Success)
                {
                    from = parseFrom.Value;
                }
            }

            LocalDate? to = null;
            if (!string.IsNullOrEmpty(request.To))
            {
                ParseResult<LocalDate> parseTo = LocalDatePattern.Iso.Parse(request.To);
                if (parseTo.Success)
                {
                    to = parseTo.Value;
                }
            }

            GetCustomerFeedbackQuery? query = new GetCustomerFeedbackQuery(
                restaurantId,
                request.OrderId,
                request.MinRating,
                request.MaxRating,
                from,
                to,
                request.RewardRedeemed,
                request.PageIndex,
                request.PageSize);

            GetCustomerFeedbackResult? result = await sender.Send(query);
            return Results.Ok(result.Adapt<GetCustomerFeedbackResponse>());
        })
        .WithDescription("Gets customer feedback entries for a restaurant.")
        .WithName("GetCustomerFeedback")
        .Produces<GetCustomerFeedbackResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedback;

/// <remarks>
/// <c>From</c>/<c>To</c> are <see cref="DateOnly"/> rather than NodaTime
/// <c>LocalDate</c> because Minimal API can only bind an <c>[AsParameters]</c>
/// property from the query string when its type is parsable. <c>LocalDate</c>
/// is not, so the binder infers it as a body parameter and throws on GET while
/// building the endpoint graph — which fails every route in the service. Same
/// ISO-8601 wire format; converted to <c>LocalDate</c> at the handler boundary.
/// </remarks>
public record GetCustomerFeedbackRequest(
    Guid? OrderId,
    int? MinRating,
    int? MaxRating,
    DateOnly? From,
    DateOnly? To,
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
                request.From is { } from ? LocalDate.FromDateOnly(from) : null,
                request.To is { } to ? LocalDate.FromDateOnly(to) : null,
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
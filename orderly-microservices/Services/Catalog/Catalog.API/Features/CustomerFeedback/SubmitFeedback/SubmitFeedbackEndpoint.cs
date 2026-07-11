namespace Catalog.API.Features.CustomerFeedback.SubmitFeedback;

public record SubmitFeedbackRequest(
    Guid OrderId,
    int OverallRating,
    int FoodQualityRating,
    int ServiceSpeedRating,
    int WaiterFriendlinessRating,
    string? Comments);

public record SubmitFeedbackResponse(
    int Id,
    string RewardCode,
    string RewardType,
    string RewardDescription,
    decimal? RewardValue);

public class SubmitFeedbackEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("CustomerFeedback");

        group.MapPost("/restaurants/{restaurantId}/feedback", async (
            Guid restaurantId,
            SubmitFeedbackRequest request,
            ISender sender) =>
        {
            var command = new SubmitFeedbackCommand(
                restaurantId,
                request.OrderId,
                request.OverallRating,
                request.FoodQualityRating,
                request.ServiceSpeedRating,
                request.WaiterFriendlinessRating,
                request.Comments);
            var result = await sender.Send(command);
            return Results.Created($"/api/v1/restaurants/{restaurantId}/feedback/{result.Id}", result.Adapt<SubmitFeedbackResponse>());
        })
        .WithDescription("Submits a customer's post-visit feedback. Issues a reward and queues FeedbackSubmittedIntegrationEvent on OverallRating ≥ 4.")
        .WithName("SubmitFeedback")
        .Produces<SubmitFeedbackResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
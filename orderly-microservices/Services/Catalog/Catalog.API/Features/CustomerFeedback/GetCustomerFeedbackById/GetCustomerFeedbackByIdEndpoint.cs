using Catalog.API.Exceptions;
using Catalog.API.Features.CustomerFeedback.GetCustomerFeedback;

namespace Catalog.API.Features.CustomerFeedback.GetCustomerFeedbackById;

public record GetCustomerFeedbackByIdRequest(int Id);

public record GetCustomerFeedbackByIdResponse(CustomerFeedbackDto Item);

public class GetCustomerFeedbackByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("CustomerFeedback");

        group.MapGet("/restaurants/{restaurantId}/feedback/{id:int}", async (
            Guid restaurantId,
            int id,
            ISender sender) =>
        {
            var query = new GetCustomerFeedbackByIdQuery(id, restaurantId);
            var result = await sender.Send(query);
            return Results.Ok(new GetCustomerFeedbackByIdResponse(result.Item));
        })
        .WithDescription("Gets a single customer feedback entry by ID.")
        .WithName("GetCustomerFeedbackById")
        .Produces<GetCustomerFeedbackByIdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
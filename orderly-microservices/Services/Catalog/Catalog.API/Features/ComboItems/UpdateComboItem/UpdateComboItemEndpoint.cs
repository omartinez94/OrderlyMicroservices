namespace Catalog.API.Features.ComboItems.UpdateComboItem;

public record UpdateComboItemRequest(int Quantity, bool IsOptional);

public record UpdateComboItemResponse(bool Success);

public class UpdateComboItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("ComboItems");

        group.MapPut("/combo-items/{id:int}", async (int id, UpdateComboItemRequest request, ISender sender) =>
        {
            await sender.Send(new UpdateComboItemCommand(id, request.Quantity, request.IsOptional));
            return Results.Ok(new UpdateComboItemResponse(true));
        })
        .WithDescription("Updates an existing combo item row (quantity and/or isOptional).")
        .WithName("UpdateComboItem")
        .Produces<UpdateComboItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
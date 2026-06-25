namespace Catalog.API.Features.ComboItems.DeleteComboItem;

public record DeleteComboItemResponse(bool Success);

public class DeleteComboItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("ComboItems");

        group.MapDelete("/combo-items/{id}", async (int id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteComboItemCommand(id));
            var response = result.Adapt<DeleteComboItemResponse>();

            return Results.Ok(response);
        })
        .WithDescription("Deletes a combo item.")
        .WithName("DeleteComboItem")
        .Produces<DeleteComboItemResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

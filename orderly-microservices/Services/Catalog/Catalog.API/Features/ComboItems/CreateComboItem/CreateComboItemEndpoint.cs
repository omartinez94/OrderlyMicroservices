namespace Catalog.API.Features.ComboItems.CreateComboItem;

public record CreateComboItemRequest(
    Guid IncludedMenuItemId,
    int Quantity,
    bool IsOptional);

public record CreateComboItemResponse(int Id);

public class CreateComboItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("ComboItems");

        group.MapPost("/menu-items/{comboMenuItemId}/combo-items", async (Guid comboMenuItemId, CreateComboItemRequest request, ISender sender) =>
        {
            var command = new CreateComboItemCommand(comboMenuItemId, request.IncludedMenuItemId, request.Quantity, request.IsOptional);

            var result = await sender.Send(command);
            var response = result.Adapt<CreateComboItemResponse>();

            return Results.Created($"/api/v1/menu-items/{comboMenuItemId}/combo-items/{response.Id}", response);
        })
        .WithDescription("Creates a new combo item.")
        .WithName("CreateComboItem")
        .Produces<CreateComboItemResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}

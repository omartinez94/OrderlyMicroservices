namespace Catalog.API.Features.BulkOrderUploads.UploadBulkOrder;

public record UploadBulkOrderRequest(
    string FileName,
    IReadOnlyList<UploadBulkOrderRow> Rows);

public record UploadBulkOrderResponse(int Id, int FailedRows, int SuccessfulRows, int TotalRows);

public class UploadBulkOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("BulkOrderUploads");

        group.MapPost("/restaurants/{restaurantId}/bulk-order-uploads", async (
            Guid restaurantId,
            UploadBulkOrderRequest request,
            ISender sender) =>
        {
            var command = new UploadBulkOrderCommand(restaurantId, request.FileName, request.Rows);
            var result = await sender.Send(command);
            return Results.Created($"/api/v1/restaurants/{restaurantId}/bulk-order-uploads/{result.Id}", result.Adapt<UploadBulkOrderResponse>());
        })
        .WithDescription("Accepts a batch of parsed order rows and persists a BulkOrderUpload envelope for approval.")
        .WithName("UploadBulkOrder")
        .Produces<UploadBulkOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
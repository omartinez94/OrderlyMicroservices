namespace Catalog.API.Features.BulkOrderUploads.RejectBulkOrderUpload;

public record RejectBulkOrderUploadRequest(string? Reason);

public record RejectBulkOrderUploadResponse(int Id, BulkUploadStatus Status);

public class RejectBulkOrderUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("BulkOrderUploads");

        group.MapPost("/restaurants/{restaurantId}/bulk-order-uploads/{id:int}/reject", async (
            Guid restaurantId,
            int id,
            RejectBulkOrderUploadRequest request,
            ISender sender) =>
        {
            var result = await sender.Send(new RejectBulkOrderUploadCommand(restaurantId, id, request.Reason));
            return Results.Ok(result.Adapt<RejectBulkOrderUploadResponse>());
        })
        .WithDescription("Rejects a previously-uploaded bulk batch. Idempotent on already-failed uploads.")
        .WithName("RejectBulkOrderUpload")
        .Produces<RejectBulkOrderUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
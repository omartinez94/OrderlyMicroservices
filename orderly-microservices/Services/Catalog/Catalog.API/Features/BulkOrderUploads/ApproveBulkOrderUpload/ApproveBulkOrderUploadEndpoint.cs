namespace Catalog.API.Features.BulkOrderUploads.ApproveBulkOrderUpload;

public record ApproveBulkOrderUploadResponse(int Id, BulkUploadStatus Status, Instant ApprovedAt);

public class ApproveBulkOrderUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("BulkOrderUploads");

        group.MapPost("/restaurants/{restaurantId}/bulk-order-uploads/{id:int}/approve", async (
            Guid restaurantId,
            int id,
            ISender sender) =>
        {
            var result = await sender.Send(new ApproveBulkOrderUploadCommand(restaurantId, id));
            return Results.Ok(result.Adapt<ApproveBulkOrderUploadResponse>());
        })
        .WithDescription("Approves a previously-uploaded bulk batch. Idempotent on already-completed uploads.")
        .WithName("ApproveBulkOrderUpload")
        .Produces<ApproveBulkOrderUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
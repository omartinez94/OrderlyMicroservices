namespace Catalog.API.Features.BulkOrderUploads.ApproveBulkOrderUpload;

public record ApproveBulkOrderUploadResponse(int Id, BulkUploadStatus Status, Instant ApprovedAt);

public class ApproveBulkOrderUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder? group = app.MapGroup("/v1").WithTags("BulkOrderUploads");

        group.MapPost("/restaurants/{restaurantId}/bulk-order-uploads/{id:int}/approve", async (
            Guid restaurantId,
            int id,
            ISender sender) =>
        {
            ApproveBulkOrderUploadResult? result = await sender.Send(new ApproveBulkOrderUploadCommand(restaurantId, id));
            return Results.Ok(result.Adapt<ApproveBulkOrderUploadResponse>());
        })
        .RequirePermission("catalog:menu_update")
        .WithDescription("Approves a previously-uploaded bulk batch. Idempotent on already-completed uploads.")
        .WithName("ApproveBulkOrderUpload")
        .Produces<ApproveBulkOrderUploadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
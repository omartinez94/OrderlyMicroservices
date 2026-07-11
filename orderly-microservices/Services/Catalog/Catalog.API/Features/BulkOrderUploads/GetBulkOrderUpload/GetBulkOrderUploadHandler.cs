using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.BulkOrderUploads.GetBulkOrderUpload;

public record GetBulkOrderUploadQuery(Guid RestaurantId, int Id) : IQuery<GetBulkOrderUploadResult>;

public record GetBulkOrderUploadResult(BulkOrderUploadDto Upload);

public record BulkOrderUploadDto(
    int Id,
    Guid RestaurantId,
    string FileName,
    int TotalRows,
    int SuccessfulRows,
    int FailedRows,
    BulkUploadStatus Status,
    string ErrorLog,
    Guid UploadedByUserId,
    Instant CreatedAt,
    Instant? ApprovedAt,
    Guid? ApprovedByAdminId,
    Instant? CompletedAt);

public class GetBulkOrderUploadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("BulkOrderUploads");

        group.MapGet("/restaurants/{restaurantId}/bulk-order-uploads/{id:int}", async (
            Guid restaurantId,
            int id,
            ISender sender) =>
        {
            var result = await sender.Send(new GetBulkOrderUploadQuery(restaurantId, id));
            return Results.Ok(result.Upload);
        })
        .WithDescription("Gets a single bulk order upload by ID.")
        .WithName("GetBulkOrderUpload")
        .Produces<BulkOrderUploadDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

internal class GetBulkOrderUploadQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetBulkOrderUploadQuery, GetBulkOrderUploadResult>
{
    public async Task<GetBulkOrderUploadResult> Handle(GetBulkOrderUploadQuery query, CancellationToken cancellationToken)
    {
        var upload = await dbContext.BulkOrderUploads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.RestaurantId == query.RestaurantId, cancellationToken)
            ?? throw new BulkOrderUploadNotFoundException(query.Id);

        return new GetBulkOrderUploadResult(upload.Adapt<BulkOrderUploadDto>());
    }
}
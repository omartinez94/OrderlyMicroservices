using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.BulkOrderUploads.ApproveBulkOrderUpload;

/// <summary>
/// Manager / admin approval of a previously-uploaded bulk batch. Sets
/// <see cref="BulkOrderUpload.Status"/> = <see cref="BulkUploadStatus.Completed"/>,
/// stamps <see cref="BulkOrderUpload.ApprovedAt"/> and
/// <see cref="BulkOrderUpload.ApprovedByAdminId"/>. Idempotent on
/// <see cref="BulkUploadStatus.Completed"/>.
/// </summary>
public record ApproveBulkOrderUploadCommand(Guid RestaurantId, int Id) : ICommand<ApproveBulkOrderUploadResult>;

public record ApproveBulkOrderUploadResult(int Id, BulkUploadStatus Status, Instant ApprovedAt);

public class ApproveBulkOrderUploadCommandValidator : AbstractValidator<ApproveBulkOrderUploadCommand>
{
    public ApproveBulkOrderUploadCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
    }
}

internal class ApproveBulkOrderUploadCommandHandler(
    CatalogDbContext dbContext,
    ICurrentUser currentUser,
    ILogger<ApproveBulkOrderUploadCommandHandler> logger) : ICommandHandler<ApproveBulkOrderUploadCommand, ApproveBulkOrderUploadResult>
{
    public async Task<ApproveBulkOrderUploadResult> Handle(ApproveBulkOrderUploadCommand command, CancellationToken cancellationToken)
    {
        var upload = await dbContext.BulkOrderUploads
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.RestaurantId == command.RestaurantId, cancellationToken)
            ?? throw new BulkOrderUploadNotFoundException(command.Id);

        if (upload.Status == BulkUploadStatus.Completed)
        {
            // Idempotent — return the existing approval timestamp.
            return new ApproveBulkOrderUploadResult(upload.Id, upload.Status, upload.ApprovedAt!.Value);
        }

        if (upload.Status == BulkUploadStatus.Failed)
        {
            throw new BadRequestException($"BulkOrderUpload {upload.Id} failed validation and cannot be approved");
        }

        upload.Status = BulkUploadStatus.Completed;
        upload.ApprovedAt = SystemClock.Instance.GetCurrentInstant();
        upload.CompletedAt = upload.ApprovedAt;
        upload.ApprovedByAdminId = currentUser.UserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "BulkOrderUpload {UploadId} approved by {AdminId} for restaurant {RestaurantId}",
            upload.Id, currentUser.UserId, command.RestaurantId);

        return new ApproveBulkOrderUploadResult(upload.Id, upload.Status, upload.ApprovedAt.Value);
    }
}
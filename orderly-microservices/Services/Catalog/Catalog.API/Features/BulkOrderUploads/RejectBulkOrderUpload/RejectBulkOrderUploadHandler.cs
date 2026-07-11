using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.BulkOrderUploads.RejectBulkOrderUpload;

/// <summary>
/// Manager / admin rejection of a previously-uploaded bulk batch. Marks
/// the row <see cref="BulkUploadStatus.Failed"/> and stamps
/// <see cref="BulkOrderUpload.ApprovedByAdminId"/> with the rejecting admin
/// (the column doubles as "decided by"). The optional <paramref name="Reason"/>
/// is appended to <see cref="BulkOrderUpload.ErrorLog"/> so the original
/// per-row errors remain visible alongside the rejection reason.
/// </summary>
public record RejectBulkOrderUploadCommand(Guid RestaurantId, int Id, string? Reason)
    : ICommand<RejectBulkOrderUploadResult>;

public record RejectBulkOrderUploadResult(int Id, BulkUploadStatus Status);

public class RejectBulkOrderUploadCommandValidator : AbstractValidator<RejectBulkOrderUploadCommand>
{
    public RejectBulkOrderUploadCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0");
        RuleFor(x => x.Reason!).MaximumLength(500).When(x => x.Reason is not null);
    }
}

internal class RejectBulkOrderUploadCommandHandler(
    CatalogDbContext dbContext,
    ICurrentUser currentUser,
    ILogger<RejectBulkOrderUploadCommandHandler> logger) : ICommandHandler<RejectBulkOrderUploadCommand, RejectBulkOrderUploadResult>
{
    public async Task<RejectBulkOrderUploadResult> Handle(RejectBulkOrderUploadCommand command, CancellationToken cancellationToken)
    {
        var upload = await dbContext.BulkOrderUploads
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.RestaurantId == command.RestaurantId, cancellationToken)
            ?? throw new BulkOrderUploadNotFoundException(command.Id);

        if (upload.Status == BulkUploadStatus.Failed)
        {
            return new RejectBulkOrderUploadResult(upload.Id, upload.Status);
        }

        upload.Status = BulkUploadStatus.Failed;
        upload.ApprovedByAdminId = currentUser.UserId;
        upload.CompletedAt = SystemClock.Instance.GetCurrentInstant();

        if (!string.IsNullOrWhiteSpace(command.Reason))
        {
            var existing = upload.ErrorLog ?? string.Empty;
            upload.ErrorLog = string.IsNullOrEmpty(existing)
                ? $"Rejection: {command.Reason}"
                : $"{existing}\nRejection: {command.Reason}";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "BulkOrderUpload {UploadId} rejected by {AdminId} for restaurant {RestaurantId}",
            upload.Id, currentUser.UserId, command.RestaurantId);

        return new RejectBulkOrderUploadResult(upload.Id, upload.Status);
    }
}
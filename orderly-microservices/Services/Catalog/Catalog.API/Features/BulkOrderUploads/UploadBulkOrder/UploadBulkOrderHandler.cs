using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.BulkOrderUploads.UploadBulkOrder;

/// <summary>
/// Accepts a batch of order rows from the operator (parsed client-side
/// into JSON) and persists a <see cref="BulkOrderUpload"/> row in
/// <see cref="BulkUploadStatus.Pending"/>. The handler runs lightweight
/// validation (menu item ids exist, table availability) and writes any
/// per-row errors into <see cref="BulkOrderUpload.ErrorLog"/> as a JSON
/// array — the actual order creation lives in Ordering; this slice
/// just records the batch envelope.
/// </summary>
/// <param name="RestaurantId">Tenant scope for the batch.</param>
/// <param name="FileName">Original file name (for audit trail).</param>
/// <param name="Rows">Parsed rows (one JSON object per CSV / Excel row).</param>
public record UploadBulkOrderCommand(
    Guid RestaurantId,
    string FileName,
    IReadOnlyList<UploadBulkOrderRow> Rows) : ICommand<UploadBulkOrderResult>;

/// <summary>One parsed row from the operator's upload.</summary>
/// <param name="MenuItemId">The menu item the customer ordered.</param>
/// <param name="TableId">Optional table the order is for.</param>
/// <param name="Quantity">Units ordered.</param>
/// <param name="Notes">Free-text row note (capped to 500 chars by validator).</param>
public record UploadBulkOrderRow(
    Guid MenuItemId,
    Guid? TableId,
    int Quantity,
    string? Notes);

public record UploadBulkOrderResult(int Id, int FailedRows, int SuccessfulRows, int TotalRows);

public class UploadBulkOrderCommandValidator : AbstractValidator<UploadBulkOrderCommand>
{
    public UploadBulkOrderCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty().WithMessage("RestaurantId is required");
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255).WithMessage("FileName is required");
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Rows must contain at least one row");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.MenuItemId).NotEmpty().WithMessage("MenuItemId is required");
            row.RuleFor(r => r.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0");
            row.RuleFor(r => r.Notes!).MaximumLength(500).When(r => r.Notes is not null);
        });
    }
}

internal class UploadBulkOrderCommandHandler(
    CatalogDbContext dbContext,
    ICurrentUser currentUser,
    ILogger<UploadBulkOrderCommandHandler> logger) : ICommandHandler<UploadBulkOrderCommand, UploadBulkOrderResult>
{
    public async Task<UploadBulkOrderResult> Handle(UploadBulkOrderCommand command, CancellationToken cancellationToken)
    {
        var menuItemIds = command.Rows.Select(r => r.MenuItemId).Distinct().ToList();
        var tableIds = command.Rows.Where(r => r.TableId.HasValue).Select(r => r.TableId!.Value).Distinct().ToList();

        // Batch lookup — one round trip per referenced table.
        var validMenuItemIds = await dbContext.MenuItems
            .Where(m => menuItemIds.Contains(m.Id) && !m.IsDeleted)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        var validMenuItemIdSet = validMenuItemIds.ToHashSet();

        var unavailableTableIds = tableIds.Count == 0
            ? new HashSet<Guid>()
            : (await dbContext.Tables
                .Where(t => tableIds.Contains(t.Id) && t.RestaurantId == command.RestaurantId && t.Status != TableStatus.Available)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        var errors = new List<string>();
        var successfulRows = 0;
        for (var i = 0; i < command.Rows.Count; i++)
        {
            var row = command.Rows[i];
            if (!validMenuItemIdSet.Contains(row.MenuItemId))
            {
                errors.Add($"Row {i + 1}: MenuItem {row.MenuItemId} not found or deleted");
                continue;
            }
            if (row.TableId.HasValue && unavailableTableIds.Contains(row.TableId.Value))
            {
                errors.Add($"Row {i + 1}: Table {row.TableId} not available");
                continue;
            }
            successfulRows++;
        }

        var upload = new BulkOrderUpload
        {
            RestaurantId = command.RestaurantId,
            FileName = command.FileName,
            TotalRows = command.Rows.Count,
            SuccessfulRows = successfulRows,
            FailedRows = errors.Count,
            ErrorLog = JsonSerializer.Serialize(errors),
            Status = errors.Count == command.Rows.Count
                ? BulkUploadStatus.Failed
                : BulkUploadStatus.Pending,
            UploadedByUserId = currentUser.UserId ?? Guid.Empty,
        };

        dbContext.BulkOrderUploads.Add(upload);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "BulkOrderUpload {UploadId} accepted: {Successful}/{Total} rows succeeded",
            upload.Id, successfulRows, command.Rows.Count);

        return new UploadBulkOrderResult(upload.Id, errors.Count, successfulRows, command.Rows.Count);
    }
}
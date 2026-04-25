namespace Catalog.API.Models;

public class BulkOrderUpload : Entity<int>
{
    public Guid RestaurantId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessfulRows { get; set; }
    public int FailedRows { get; set; }
    public BulkUploadStatus Status { get; set; } = BulkUploadStatus.Pending;
    public string ErrorLog { get; set; } = string.Empty; // jsonb
    public Guid? ApprovedByAdminId { get; set; }
    public Instant? ApprovedAt { get; set; }
    public Instant? CompletedAt { get; set; }
}

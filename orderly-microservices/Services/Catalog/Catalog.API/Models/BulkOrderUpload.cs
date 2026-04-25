namespace Catalog.API.Models;

public class BulkOrderUpload : Entity<int>
{
    public string ErrorLog { get; set; } = string.Empty; // jsonb
    public int FailedRows { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid RestaurantId { get; set; }
    public BulkUploadStatus Status { get; set; } = BulkUploadStatus.Pending;
    public int SuccessfulRows { get; set; }
    public int TotalRows { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Instant? ApprovedAt { get; set; }
    public Guid? ApprovedByAdminId { get; set; }
    public Instant? CompletedAt { get; set; }
}

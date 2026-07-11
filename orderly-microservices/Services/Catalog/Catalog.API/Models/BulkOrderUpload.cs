namespace Catalog.API.Models;

/// <summary>
/// Operator-uploaded bulk order batch. The CSV/Excel content is parsed
/// out-of-band by the upload handler; this row stores only the batch
/// summary (counts, status, error log) so the approve / reject workflow
/// can run without re-parsing the source file.
/// </summary>
/// <remarks>
/// Base is <c>AuditableEntity&lt;int&gt;</c>. Operators use the
/// audit columns to trace who uploaded and approved a given batch.
/// </remarks>
public class BulkOrderUpload : AuditableEntity<int>
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

namespace BuildingBlocks.Enums;

public enum RecipientType
{
    Customer,
    Staff,
    Manager
}

public enum NotificationChannel
{
    Email,
    WhatsApp,
    Sms
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}

public enum BulkUploadStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

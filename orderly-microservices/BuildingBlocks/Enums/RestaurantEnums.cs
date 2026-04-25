namespace BuildingBlocks.Enums;

public enum Role
{
    Admin,
    Manager,
    Waiter,
    KitchenStaff
}

public enum TableStatus
{
    Available,
    Occupied,
    Reserved,
    Cleaning,
    NeedsAttention
}

public enum ReservationStatus
{
    Pending,
    Confirmed,
    Seated,
    Completed,
    Cancelled,
    NoShow
}

public enum WalkInQueueStatus
{
    Waiting,
    Notified,
    Seated,
    Cancelled,
    NoShow
}

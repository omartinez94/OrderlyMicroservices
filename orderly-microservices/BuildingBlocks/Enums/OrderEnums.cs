namespace BuildingBlocks.Enums;

public enum OrderStatus
{
    Ordering,
    Pending,
    Confirmed,
    Preparing,
    Ready,
    Delivered,
    Completed,
    Cancelled,
    OnHold
}

public enum OrderType
{
    DineIn,
    Takeout,
    Delivery
}

public enum DeliveryStatus
{
    Pending,
    Dispatched,
    Delivered
}

public enum PrepStatus
{
    Pending,
    Preparing,
    Ready
}

public enum SplitType
{
    Equal,
    Custom
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Void
}

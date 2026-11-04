namespace Ordering.Application.Dashboard.Dtos;

public record RestaurantOperationsDashboardDto(
    Instant ServerTime,
    Guid RestaurantId,
    string Currency,
    string TimeZone,
    LocalDate BusinessDate,
    DashboardMetaDto Meta,
    LiveOperationalTickerDto? Ticker,
    SalesPaceDto? SalesPace,
    KitchenVelocitySnapshotDto? KitchenVelocity,
    FloorPulseSnapshotDto? FloorPulse,
    IReadOnlyList<OpenCheckDto> OpenChecks,
    IReadOnlyList<WaitlistEntryDto> Waitlist,
    IReadOnlyList<OperationalAlertDto> ActiveAlerts);

namespace Assistant.API.Features.Snapshots;

public sealed record RestaurantOperationalSnapshotDto(
    Guid RestaurantId,
    IReadOnlyList<string> EightySixedItems,
    IReadOnlyList<string> LowStockIngredients,
    int ActiveKitchenTickets,
    int DelayedKitchenTicketsOver15Min,
    int ActiveOccupiedTables,
    int AvailableTables,
    Instant UpdatedAt)
{
    public string ToCompactSystemPromptDigest()
    {
        string eightySixText = EightySixedItems.Count > 0
            ? string.Join(", ", EightySixedItems)
            : "None";
        string lowStockText = LowStockIngredients.Count > 0
            ? string.Join(", ", LowStockIngredients)
            : "None";

        return $"[LIVE RESTAURANT SNAPSHOT ({RestaurantId}) | 86'd Items: {eightySixText} | Low Stock: {lowStockText} | KDS Active Tickets: {ActiveKitchenTickets} (Delayed >15m: {DelayedKitchenTicketsOver15Min}) | Tables: {ActiveOccupiedTables} occupied / {AvailableTables} available]";
    }
}

public interface IRestaurantContextSnapshotService
{
    Task<RestaurantOperationalSnapshotDto> GetSnapshotAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);

    Task UpdateEightySixedItemAsync(
        Guid restaurantId,
        string itemName,
        bool isAvailable,
        CancellationToken cancellationToken = default);

    Task UpdateLowStockIngredientAsync(
        Guid restaurantId,
        string ingredientName,
        bool isAvailable,
        CancellationToken cancellationToken = default);

    Task RecordOrderActivityAsync(
        Guid restaurantId,
        int activeTicketDelta,
        int delayedTicketCount,
        CancellationToken cancellationToken = default);

    Task UpdateTableOccupancyAsync(
        Guid restaurantId,
        int occupiedTables,
        int availableTables,
        CancellationToken cancellationToken = default);
}

namespace Kitchen.API.Domain.Aggregates.KitchenStation;

/// <summary>
/// Lightweight catalog entity (id, name, sort order, active flag) per
/// restaurant. Used for routing tickets to a station group on the SignalR
/// hub and for filtering the kitchen queue.
/// </summary>
public class KitchenStation : Entity<StationId>
{
    // EF Core.
    private KitchenStation() { }

    public KitchenStation(
        StationId id,
        Guid restaurantId,
        string name,
        int sortOrder,
        bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (restaurantId == Guid.Empty)
            throw new KitchenDomainException("restaurantId cannot be empty.", nameof(restaurantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        RestaurantId = restaurantId;
        Name = name;
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public Guid RestaurantId { get; private set; }
    public string Name { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
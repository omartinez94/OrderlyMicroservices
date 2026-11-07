using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

namespace Assistant.API.Features.Snapshots;

/// <summary>
/// Maintains compact (~150-token) per-restaurant live operational snapshots in Redis
/// (<c>ai:snapshot:{restaurantId}</c>, 15-minute TTL) updated asynchronously by MassTransit consumers.
/// Eliminates repetitive multi-turn read tool round-trips for high-frequency status questions.
/// </summary>
public sealed class RestaurantContextSnapshotService(
    IClock clock,
    IConnectionMultiplexer? redis = null) : IRestaurantContextSnapshotService
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    private readonly ConcurrentDictionary<Guid, object> _locks = new();
    private readonly ConcurrentDictionary<Guid, RestaurantOperationalSnapshotDto> _memorySnapshots = new();

    public static string GetSnapshotKey(Guid restaurantId) => $"ai:snapshot:{restaurantId}";

    public async Task<RestaurantOperationalSnapshotDto> GetSnapshotAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId is required.", nameof(restaurantId));
        }

        if (redis is { IsConnected: true })
        {
            IDatabase db = redis.GetDatabase();
            RedisValue raw = await db.StringGetAsync(GetSnapshotKey(restaurantId)).ConfigureAwait(false);
            if (raw.HasValue)
            {
                RestaurantOperationalSnapshotDto? deserialized =
                    JsonSerializer.Deserialize<RestaurantOperationalSnapshotDto>(raw.ToString(), JsonOptions);
                if (deserialized is not null)
                {
                    return deserialized;
                }
            }
        }

        if (_memorySnapshots.TryGetValue(restaurantId, out RestaurantOperationalSnapshotDto? cached))
        {
            return cached;
        }

        RestaurantOperationalSnapshotDto initial = CreateDefaultSnapshot(restaurantId, clock.GetCurrentInstant());
        _memorySnapshots[restaurantId] = initial;
        return initial;
    }

    public Task UpdateEightySixedItemAsync(
        Guid restaurantId,
        string itemName,
        bool isAvailable,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty || string.IsNullOrWhiteSpace(itemName))
        {
            return Task.CompletedTask;
        }

        return MutateSnapshotAsync(restaurantId, current =>
        {
            HashSet<string> items = new(current.EightySixedItems, StringComparer.OrdinalIgnoreCase);
            if (isAvailable)
            {
                items.Remove(itemName.Trim());
            }
            else
            {
                items.Add(itemName.Trim());
            }

            return current with
            {
                EightySixedItems = items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                UpdatedAt = clock.GetCurrentInstant()
            };
        });
    }

    public Task UpdateLowStockIngredientAsync(
        Guid restaurantId,
        string ingredientName,
        bool isAvailable,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty || string.IsNullOrWhiteSpace(ingredientName))
        {
            return Task.CompletedTask;
        }

        return MutateSnapshotAsync(restaurantId, current =>
        {
            HashSet<string> ingredients = new(current.LowStockIngredients, StringComparer.OrdinalIgnoreCase);
            if (isAvailable)
            {
                ingredients.Remove(ingredientName.Trim());
            }
            else
            {
                ingredients.Add(ingredientName.Trim());
            }

            return current with
            {
                LowStockIngredients = ingredients.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                UpdatedAt = clock.GetCurrentInstant()
            };
        });
    }

    public Task RecordOrderActivityAsync(
        Guid restaurantId,
        int activeTicketDelta,
        int delayedTicketCount,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return MutateSnapshotAsync(restaurantId, current =>
        {
            int nextActive = Math.Max(0, current.ActiveKitchenTickets + activeTicketDelta);
            int nextDelayed = delayedTicketCount >= 0
                ? delayedTicketCount
                : current.DelayedKitchenTicketsOver15Min;

            return current with
            {
                ActiveKitchenTickets = nextActive,
                DelayedKitchenTicketsOver15Min = Math.Min(nextActive, nextDelayed),
                UpdatedAt = clock.GetCurrentInstant()
            };
        });
    }

    public Task UpdateTableOccupancyAsync(
        Guid restaurantId,
        int occupiedTables,
        int availableTables,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return MutateSnapshotAsync(restaurantId, current => current with
        {
            ActiveOccupiedTables = Math.Max(0, occupiedTables),
            AvailableTables = Math.Max(0, availableTables),
            UpdatedAt = clock.GetCurrentInstant()
        });
    }

    private static RestaurantOperationalSnapshotDto CreateDefaultSnapshot(Guid restaurantId, Instant now) =>
        new(
            RestaurantId: restaurantId,
            EightySixedItems: [],
            LowStockIngredients: [],
            ActiveKitchenTickets: 0,
            DelayedKitchenTicketsOver15Min: 0,
            ActiveOccupiedTables: 0,
            AvailableTables: 12,
            UpdatedAt: now);

    private async Task MutateSnapshotAsync(
        Guid restaurantId,
        Func<RestaurantOperationalSnapshotDto, RestaurantOperationalSnapshotDto> mutator)
    {
        object gate = _locks.GetOrAdd(restaurantId, _ => new object());
        RestaurantOperationalSnapshotDto updated;

        lock (gate)
        {
            RestaurantOperationalSnapshotDto current = _memorySnapshots.GetOrAdd(
                restaurantId,
                id => CreateDefaultSnapshot(id, clock.GetCurrentInstant()));
            updated = mutator(current);
            _memorySnapshots[restaurantId] = updated;
        }

        if (redis is { IsConnected: true })
        {
            IDatabase db = redis.GetDatabase();
            string payload = JsonSerializer.Serialize(updated, JsonOptions);
            await db.StringSetAsync(GetSnapshotKey(restaurantId), payload, SnapshotTtl).ConfigureAwait(false);
        }
    }
}

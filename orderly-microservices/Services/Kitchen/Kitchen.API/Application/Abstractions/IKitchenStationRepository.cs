namespace Kitchen.API.Application.Abstractions;

public interface IKitchenStationRepository
{
    Task<KitchenStation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KitchenStation>> ListByRestaurantAsync(
        Guid restaurantId,
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task AddAsync(KitchenStation station, CancellationToken cancellationToken = default);
}
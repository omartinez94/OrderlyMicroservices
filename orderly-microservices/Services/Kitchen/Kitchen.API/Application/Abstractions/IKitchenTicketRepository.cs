namespace Kitchen.API.Application.Abstractions;

/// <summary>
/// Read/write boundary for <c>KitchenTicket</c> aggregates. Implemented in
/// <c>Infrastructure/Repositories/KitchenTicketRepository.cs</c>. The interface
/// exposes only the methods the application layer needs; persistence concerns
/// (DbContext, change tracking, migrations) stay inside the infrastructure
/// project.
/// </summary>
public interface IKitchenTicketRepository
{
    Task<KitchenTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the kitchen queue: tickets in <c>New</c> or <c>InProgress</c>
    /// status, optionally filtered by restaurant and station, ordered by
    /// <c>ReceivedAt</c> ascending (oldest first).
    /// </summary>
    Task<IReadOnlyList<KitchenTicket>> GetQueueAsync(
        Guid? restaurantId,
        Guid? stationId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task AddAsync(KitchenTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the aggregate as modified so the next <c>SaveChangesAsync</c>
    /// emits the UPDATE. EF tracks new aggregates automatically, but detached
    /// ones (e.g. rebuilt from a bus event) need an explicit <c>Update</c>.
    /// </summary>
    void Update(KitchenTicket ticket);
}
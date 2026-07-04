namespace Kitchen.API.Domain.Exceptions;

/// <summary>
/// Thrown when a <c>KitchenTicket</c> lookup by id returns no row. Mapped to
/// HTTP 404 Not Found by the global exception handler (inherits the
/// <c>NotFoundException</c> shape already wired in Catalog/Basket/Ordering).
/// </summary>
public class KitchenTicketNotFoundException(Guid id)
    : NotFoundException(nameof(Domain.Aggregates.KitchenTicket.KitchenTicket), id.ToString());
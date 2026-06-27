namespace Catalog.API.Exceptions;

public class ReservationNotFoundException(Guid id) : NotFoundException("Reservation", id)
{
}

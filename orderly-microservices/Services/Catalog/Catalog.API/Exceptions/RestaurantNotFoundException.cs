namespace Catalog.API.Exceptions;

public class RestaurantNotFoundException(Guid id) : NotFoundException("Restaurant", id)
{
}

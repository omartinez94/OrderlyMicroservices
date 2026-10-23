namespace Catalog.API.Exceptions;

public class RestaurantNotFoundException : NotFoundException
{
    public RestaurantNotFoundException(Guid id) : base("Restaurant", id)
    {
    }

    public RestaurantNotFoundException(string identifier) : base("Restaurant", identifier)
    {
    }

    public RestaurantNotFoundException(object id) : base("Restaurant", id)
    {
    }
}

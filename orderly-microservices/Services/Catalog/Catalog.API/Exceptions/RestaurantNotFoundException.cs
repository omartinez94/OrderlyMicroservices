namespace Catalog.API.Exceptions;

public class RestaurantNotFoundException : Exception
{
    public RestaurantNotFoundException(Guid id) : base($"Restaurant with Id {id} not found.")
    {
    }
}

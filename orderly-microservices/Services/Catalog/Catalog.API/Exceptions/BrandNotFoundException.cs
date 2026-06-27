namespace Catalog.API.Exceptions;

public class BrandNotFoundException(Guid id) : NotFoundException("Brand", id)
{
}

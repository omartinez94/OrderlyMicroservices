namespace Catalog.API.Exceptions;

public class BrandNotFoundException : NotFoundException
{
    public BrandNotFoundException(Guid id) : base("Brand", id)
    {
    }

    public BrandNotFoundException(string identifier) : base("Brand", identifier)
    {
    }

    public BrandNotFoundException(object id) : base("Brand", id)
    {
    }
}

namespace Catalog.API.Exceptions;

public class TableNotFoundException : NotFoundException
{
    public TableNotFoundException(Guid id) : base("Table", id)
    {
    }

    public TableNotFoundException(string identifier) : base("Table", identifier)
    {
    }

    public TableNotFoundException(object id) : base("Table", id)
    {
    }
}

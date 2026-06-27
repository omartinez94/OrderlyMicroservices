namespace Catalog.API.Exceptions;

public class TableNotFoundException(Guid id) : NotFoundException("Table", id)
{
}

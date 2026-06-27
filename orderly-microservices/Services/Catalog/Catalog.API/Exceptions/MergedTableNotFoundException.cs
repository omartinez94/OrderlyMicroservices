namespace Catalog.API.Exceptions;

public class MergedTableNotFoundException(Guid id) : NotFoundException("MergedTable", id)
{
}

namespace Catalog.API.Exceptions;

public class WalkInQueueNotFoundException(int id) : NotFoundException("WalkInQueue", id)
{
}

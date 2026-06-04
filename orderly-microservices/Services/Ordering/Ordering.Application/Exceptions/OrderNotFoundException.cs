namespace Ordering.Application.Exceptions;

public class OrderNotFoundException(string name, object key) : NotFoundException(name, key)
{
}

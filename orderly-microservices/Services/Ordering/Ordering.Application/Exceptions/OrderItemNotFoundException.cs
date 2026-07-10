namespace Ordering.Application.Exceptions;

public class OrderItemNotFoundException(string name, object key) : NotFoundException(name, key)
{
}
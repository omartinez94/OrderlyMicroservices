namespace Catalog.API.Exceptions;

public class MenuCategoryNotFoundException(int id) : NotFoundException("MenuCategory", id)
{
}

namespace Catalog.API.Exceptions;

public class MenuSubCategoryNotFoundException(int id) : NotFoundException("MenuSubCategory", id)
{
}

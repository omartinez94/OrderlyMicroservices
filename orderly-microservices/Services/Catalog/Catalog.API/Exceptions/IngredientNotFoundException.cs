using BuildingBlocks.Exceptions;

namespace Catalog.API.Exceptions;

public class IngredientNotFoundException(int id) : NotFoundException("Ingredient", id);

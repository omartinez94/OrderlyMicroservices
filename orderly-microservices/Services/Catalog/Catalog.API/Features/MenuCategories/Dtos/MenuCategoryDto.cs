namespace Catalog.API.Features.MenuCategories.Dtos;

public record MenuCategoryDto(
    int Id,
    string Name,
    string Description,
    int DisplayOrder,
    Guid RestaurantId);

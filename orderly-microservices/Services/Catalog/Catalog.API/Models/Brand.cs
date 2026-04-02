using BuildingBlocks.Entities.Contracts;

namespace Catalog.API.Models;

public class Brand : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public CuisineType CuisineType { get; set; } = CuisineType.Other;
    public bool IsActive { get; set; } = true;
}
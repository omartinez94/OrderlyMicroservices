namespace Ordering.Infrastructure.Http;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Exceptions;

public class CatalogPricingClient(
    HttpClient httpClient,
    IDistributedCache distributedCache,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CatalogPricingClient> logger)
    : ICatalogPricingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20),
    };

    public async Task<CatalogMenuSnapshot> GetMenuSnapshotAsync(Guid restaurantId, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"catalog:menu-snapshot:{restaurantId}";
        CatalogMenuSnapshot? cached = await TryGetCachedSnapshotAsync(cacheKey, restaurantId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/restaurants/{restaurantId}/menu");
            AttachAuthorization(request);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new CatalogMenuSnapshot(restaurantId, []);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Catalog menu request for restaurant {RestaurantId} returned HTTP {StatusCode}", restaurantId, response.StatusCode);
                throw new CatalogUnavailableException();
            }

            RawCatalogMenuResponse? body = await response.Content.ReadFromJsonAsync<RawCatalogMenuResponse>(JsonOptions, cancellationToken);
            if (body is null)
            {
                return new CatalogMenuSnapshot(restaurantId, []);
            }

            List<CatalogMenuItemDto> items = FlattenMenuItems(body);
            CatalogMenuSnapshot snapshot = new(restaurantId, items);

            await TryCacheSnapshotAsync(cacheKey, snapshot, restaurantId, cancellationToken);
            return snapshot;
        }
        catch (CatalogUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to fetch menu snapshot for restaurant {RestaurantId}", restaurantId);
            throw new CatalogUnavailableException();
        }
    }

    public async Task<CatalogTableDto?> GetTableAsync(Guid restaurantId, Guid tableId, CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/restaurants/{restaurantId}/tables/{tableId}");
            AttachAuthorization(request);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Catalog table request for {TableId} returned HTTP {StatusCode}", tableId, response.StatusCode);
                throw new CatalogUnavailableException();
            }

            RawTableResponse? body = await response.Content.ReadFromJsonAsync<RawTableResponse>(JsonOptions, cancellationToken);
            if (body?.Table is null)
            {
                return null;
            }

            return new CatalogTableDto(
                body.Table.Id,
                body.Table.RestaurantId,
                body.Table.TableNumber,
                body.Table.Capacity,
                body.Table.Status);
        }
        catch (CatalogUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to fetch table {TableId} for restaurant {RestaurantId}", tableId, restaurantId);
            throw new CatalogUnavailableException();
        }
    }

    private async Task<CatalogMenuSnapshot?> TryGetCachedSnapshotAsync(string cacheKey, Guid restaurantId, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? cachedBytes = await distributedCache.GetAsync(cacheKey, cancellationToken);
            if (cachedBytes is not null && cachedBytes.Length > 0)
            {
                return JsonSerializer.Deserialize<CatalogMenuSnapshot>(cachedBytes, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read menu snapshot cache for restaurant {RestaurantId}; proceeding with live fetch", restaurantId);
        }

        return null;
    }

    private async Task TryCacheSnapshotAsync(string cacheKey, CatalogMenuSnapshot snapshot, Guid restaurantId, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytesToCache = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            await distributedCache.SetAsync(cacheKey, bytesToCache, CacheOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to cache menu snapshot for restaurant {RestaurantId}", restaurantId);
        }
    }

    private static List<CatalogMenuItemDto> FlattenMenuItems(RawCatalogMenuResponse body)
    {
        List<CatalogMenuItemDto> items = [];
        foreach (RawCategoryNode category in body.Categories)
        {
            foreach (RawSubCategoryNode subCategory in category.SubCategories)
            {
                foreach (RawItemNode item in subCategory.Items)
                {
                    List<CatalogMenuItemVariationDto> variations = item.Variations
                        .Select(v => new CatalogMenuItemVariationDto(v.Id, v.Name, v.VariationValue, v.PriceModifier, v.IsDefault))
                        .ToList();

                    items.Add(new CatalogMenuItemDto(
                        item.Id,
                        item.Name,
                        item.BasePrice,
                        item.PromoPrice,
                        item.IsAvailable,
                        item.AvailabilityStatus ?? "Available",
                        variations,
                        item.DefaultCourseNumber));
                }
            }
        }

        return items;
    }

    private void AttachAuthorization(HttpRequestMessage request)
    {
        string? authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && AuthenticationHeaderValue.TryParse(authHeader, out AuthenticationHeaderValue? parsed))
        {
            request.Headers.Authorization = parsed;
        }
    }

    private sealed record RawCatalogMenuResponse(Guid RestaurantId, IReadOnlyList<RawCategoryNode> Categories);
    private sealed record RawCategoryNode(int Id, string Name, IReadOnlyList<RawSubCategoryNode> SubCategories);
    private sealed record RawSubCategoryNode(int Id, string Name, IReadOnlyList<RawItemNode> Items);
    private sealed record RawItemNode(
        Guid Id,
        string Name,
        decimal BasePrice,
        decimal? PromoPrice,
        bool IsAvailable,
        string AvailabilityStatus,
        IReadOnlyList<RawVariationNode> Variations,
        int? DefaultCourseNumber);
    private sealed record RawVariationNode(
        int Id,
        string Name,
        string VariationValue,
        decimal PriceModifier,
        bool IsDefault);

    private sealed record RawTableResponse(RawTableData Table);
    private sealed record RawTableData(
        Guid Id,
        Guid RestaurantId,
        string TableNumber,
        int Capacity,
        string Status);
}

namespace Ordering.Infrastructure.Http;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dashboard.Dtos;

public class CatalogDashboardReader(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CatalogDashboardReader> logger)
    : ICatalogDashboardReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CatalogFloorPulseResult> GetFloorPulseAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/restaurants/{restaurantId}/floor-pulse");

            string? authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && AuthenticationHeaderValue.TryParse(authHeader, out AuthenticationHeaderValue? parsed))
            {
                request.Headers.Authorization = parsed;
            }

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Catalog floor-pulse request for restaurant {RestaurantId} returned HTTP {StatusCode}", restaurantId, response.StatusCode);
                return new CatalogFloorPulseResult(null, [], null, "degraded");
            }

            CatalogFloorPulseResponse? body = await response.Content.ReadFromJsonAsync<CatalogFloorPulseResponse>(JsonOptions, cancellationToken);
            if (body is null)
            {
                return new CatalogFloorPulseResult(null, [], null, "degraded");
            }

            FloorPulseSnapshotDto pulse = new(
                body.FloorPulse.TotalTables,
                body.FloorPulse.OccupiedTables,
                body.FloorPulse.NeedsCleaningCount,
                body.FloorPulse.ReservedUpcomingCount,
                body.FloorPulse.ActiveCovers,
                body.FloorPulse.TotalCapacity,
                body.FloorPulse.AvgTableTurnMinutes,
                body.FloorPulse.WaitlistCount,
                body.FloorPulse.EstimatedWaitMinutes);

            List<WaitlistEntryDto> waitlist = body.Waitlist
                .Select(w => new WaitlistEntryDto(w.EntryId, w.PartyName, w.PartySize, w.WaitMinutes))
                .ToList();

            return new CatalogFloorPulseResult(pulse, waitlist, body.Currency, "ok");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to fetch floor-pulse for restaurant {RestaurantId}; degrading section", restaurantId);
            return new CatalogFloorPulseResult(null, [], null, "unavailable");
        }
    }

    private record CatalogFloorPulseResponse(
        CatalogFloorPulseData FloorPulse,
        IReadOnlyList<CatalogWaitlistData> Waitlist,
        string Currency);

    private record CatalogFloorPulseData(
        int TotalTables,
        int OccupiedTables,
        int NeedsCleaningCount,
        int ReservedUpcomingCount,
        int ActiveCovers,
        int TotalCapacity,
        double? AvgTableTurnMinutes,
        int WaitlistCount,
        int EstimatedWaitMinutes);

    private record CatalogWaitlistData(
        Guid EntryId,
        string PartyName,
        int PartySize,
        int WaitMinutes);
}

namespace Ordering.Infrastructure.Http;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Interfaces;
using Ordering.Application.Dashboard.Dtos;

public class KitchenDashboardReader(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<KitchenDashboardReader> logger)
    : IKitchenDashboardReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<KitchenVelocityResult> GetVelocityAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/kitchen/restaurants/{restaurantId}/velocity");

            string? authHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && AuthenticationHeaderValue.TryParse(authHeader, out AuthenticationHeaderValue? parsed))
            {
                request.Headers.Authorization = parsed;
            }

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Kitchen velocity request for restaurant {RestaurantId} returned HTTP {StatusCode}", restaurantId, response.StatusCode);
                return new KitchenVelocityResult(null, "degraded");
            }

            KitchenVelocityResponse? body = await response.Content.ReadFromJsonAsync<KitchenVelocityResponse>(JsonOptions, cancellationToken);
            if (body is null)
            {
                return new KitchenVelocityResult(null, "degraded");
            }

            List<KitchenStationVelocityDto> stations = body.Stations
                .Select(s => new KitchenStationVelocityDto(
                    s.StationId,
                    s.StationName,
                    s.ActiveTicketsCount,
                    s.AvgPrepMinutes,
                    s.IsDelayed))
                .ToList();

            KitchenVelocitySnapshotDto snapshot = new(
                body.ActiveTickets,
                body.AvgPrepMinutes,
                body.TargetPrepMinutes,
                body.P90PrepMinutes,
                stations,
                body.PassDwellCount);

            return new KitchenVelocityResult(snapshot, "ok");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Failed to fetch velocity for restaurant {RestaurantId}; degrading section", restaurantId);
            return new KitchenVelocityResult(null, "unavailable");
        }
    }

    private record KitchenVelocityResponse(
        int ActiveTickets,
        double? AvgPrepMinutes,
        double TargetPrepMinutes,
        double? P90PrepMinutes,
        IReadOnlyList<KitchenStationItem> Stations,
        int PassDwellCount);

    private record KitchenStationItem(
        Guid StationId,
        string StationName,
        int ActiveTicketsCount,
        double? AvgPrepMinutes,
        bool IsDelayed);
}

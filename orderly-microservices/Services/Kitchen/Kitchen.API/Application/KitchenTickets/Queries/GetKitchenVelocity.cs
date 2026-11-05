using Kitchen.API.Domain.Aggregates.KitchenStation;
using Kitchen.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Kitchen.API.Application.KitchenTickets.Queries;

public record KitchenStationVelocityItem(
    Guid StationId,
    string StationName,
    int ActiveTicketsCount,
    double? AvgPrepMinutes,
    bool IsDelayed);

public record KitchenVelocityResultDto(
    int ActiveTickets,
    double? AvgPrepMinutes,
    double TargetPrepMinutes,
    double? P90PrepMinutes,
    IReadOnlyList<KitchenStationVelocityItem> Stations,
    int PassDwellCount);

public record GetKitchenVelocityQuery(Guid RestaurantId) : IQuery<KitchenVelocityResultDto>;

public class GetKitchenVelocityHandler(
    KitchenDbContext dbContext,
    IClock clock)
    : IQueryHandler<GetKitchenVelocityQuery, KitchenVelocityResultDto>
{
    private const double TargetPrepMinutes = 15.0;

    public async Task<KitchenVelocityResultDto> Handle(
        GetKitchenVelocityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Instant now = clock.GetCurrentInstant();
        Instant todayStart = now.Minus(Duration.FromHours(24));

        List<KitchenTicket> tickets = await dbContext.Tickets
            .Include(t => t.Items)
            .AsNoTracking()
            .Where(t => t.RestaurantId == query.RestaurantId && t.ReceivedAt >= todayStart)
            .ToListAsync(cancellationToken);

        int activeTickets = tickets.Count(t => t.Status is KitchenTicketStatus.New or KitchenTicketStatus.InProgress);
        int passDwellCount = tickets.Count(t => t.Status == KitchenTicketStatus.Ready);

        List<double> prepMinutes = [];
        foreach (KitchenTicket t in tickets)
        {
            if (t.ReadyAt.HasValue)
            {
                Instant start = t.StartedAt ?? t.ReceivedAt;
                if (t.ReadyAt.Value >= start)
                {
                    prepMinutes.Add((t.ReadyAt.Value - start).TotalMinutes);
                }
            }
        }

        double? avgPrepMinutes = prepMinutes.Count > 0
            ? Math.Round(prepMinutes.Average(), 1)
            : null;

        double? p90PrepMinutes = null;
        if (prepMinutes.Count > 0)
        {
            prepMinutes.Sort();
            int index = (int)Math.Ceiling(0.9 * prepMinutes.Count) - 1;
            index = Math.Clamp(index, 0, prepMinutes.Count - 1);
            p90PrepMinutes = Math.Round(prepMinutes[index], 1);
        }

        List<KitchenStation> stations = await dbContext.Stations
            .AsNoTracking()
            .Where(s => s.RestaurantId == query.RestaurantId && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        List<KitchenStationVelocityItem> stationItems = [];
        foreach (KitchenStation station in stations)
        {
            int stationActiveTickets = tickets
                .Where(t => (t.Status is KitchenTicketStatus.New or KitchenTicketStatus.InProgress)
                         && t.Items.Any(i => i.StationId == station.Id.Value))
                .Count();

            List<double> stationPrepDurations = [];
            foreach (KitchenTicket t in tickets)
            {
                KitchenTicketItem? item = t.Items.FirstOrDefault(i => i.StationId == station.Id.Value);
                if (item?.ReadyAt is not null)
                {
                    Instant start = item.StartedAt ?? t.StartedAt ?? t.ReceivedAt;
                    if (item.ReadyAt.Value >= start)
                    {
                        stationPrepDurations.Add((item.ReadyAt.Value - start).TotalMinutes);
                    }
                }
            }

            double? stationAvg = stationPrepDurations.Count > 0
                ? Math.Round(stationPrepDurations.Average(), 1)
                : avgPrepMinutes;

            bool isDelayed = (stationAvg ?? 0) > TargetPrepMinutes;

            stationItems.Add(new KitchenStationVelocityItem(
                station.Id.Value,
                station.Name,
                stationActiveTickets,
                stationAvg,
                isDelayed));
        }

        return new KitchenVelocityResultDto(
            activeTickets,
            avgPrepMinutes,
            TargetPrepMinutes,
            p90PrepMinutes,
            stationItems,
            passDwellCount);
    }
}

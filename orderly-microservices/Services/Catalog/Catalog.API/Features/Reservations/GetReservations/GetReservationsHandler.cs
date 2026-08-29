using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Reservations.GetReservations;

public record GetReservationsQuery(
    Guid RestaurantId,
    LocalDate? Date = null,
    ReservationStatus? Status = null,
    int? PageNumber = 1,
    int? PageSize = 10) : IQuery<GetReservationsResult>;

public record GetReservationsResult(IEnumerable<Reservation> Reservations, int TotalCount);

internal class GetReservationsQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetReservationsQuery, GetReservationsResult>
{
    public async Task<GetReservationsResult> Handle(GetReservationsQuery query, CancellationToken cancellationToken)
    {
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 10;
        if (pageSize > 50)
        {
            pageSize = 50;
        }

        IQueryable<Reservation>? baseQuery = dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.RestaurantId == query.RestaurantId);

        if (query.Date.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.ReservationDate == query.Date.Value);
        }

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        }

        int totalCount = await EntityFrameworkQueryableExtensions.CountAsync(baseQuery, cancellationToken);

        List<Reservation>? reservations = await EntityFrameworkQueryableExtensions.ToListAsync(
            baseQuery
                .OrderBy(r => r.ReservationDate)
                .ThenBy(r => r.ReservationTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize),
            cancellationToken);

        return new GetReservationsResult(reservations, totalCount);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Features.Reservations.GetReservationById;

public record GetReservationByIdQuery(Guid Id) : IQuery<GetReservationByIdResult>;

public record GetReservationByIdResult(Reservation Reservation);

internal class GetReservationByIdQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetReservationByIdQuery, GetReservationByIdResult>
{
    public async Task<GetReservationByIdResult> Handle(GetReservationByIdQuery query, CancellationToken cancellationToken)
    {
        var reservation = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            dbContext.Reservations.AsNoTracking(),
            r => r.Id == query.Id,
            cancellationToken)
            ?? throw new ReservationNotFoundException(query.Id);

        return new GetReservationByIdResult(reservation);
    }
}

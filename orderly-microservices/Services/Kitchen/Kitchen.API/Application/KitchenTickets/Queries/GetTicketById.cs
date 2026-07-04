namespace Kitchen.API.Application.KitchenTickets.Queries;

public record GetTicketByIdQuery(Guid Id) : IQuery<KitchenTicketDto>;

public class GetTicketByIdHandler(
    IKitchenTicketRepository repository)
    : IQueryHandler<GetTicketByIdQuery, KitchenTicketDto>
{
    public async Task<KitchenTicketDto> Handle(
        GetTicketByIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        KitchenTicket ticket = await repository.GetByIdAsync(query.Id, cancellationToken)
            ?? throw new KitchenTicketNotFoundException(query.Id);

        return ticket.ToDto();
    }
}
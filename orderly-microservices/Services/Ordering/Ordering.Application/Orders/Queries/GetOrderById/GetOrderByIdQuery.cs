namespace Ordering.Application.Orders.Queries.GetOrderById;

public record GetOrderByIdQuery(string Identifier) : IQuery<GetOrderByIdResult>
{
    public GetOrderByIdQuery(Guid id) : this(id.ToString()) { }

    public Guid Id => Guid.TryParse(Identifier, out Guid guid) ? guid : Guid.Empty;
}

public record GetOrderByIdResult(OrderDto Order);

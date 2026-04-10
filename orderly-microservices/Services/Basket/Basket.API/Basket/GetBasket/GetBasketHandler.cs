namespace Basket.API.Basket.GetBasket;

public record GetBasketQuery(Guid UserId, Guid RestaurantId) : IQuery<GetBasketResult>;
public record GetBasketResult(bool IsSuccess);

public class GetBasketHandler(IDocumentSession session) : IQueryHandler<GetBasketQuery, GetBasketResult>
{
    public async Task<GetBasketResult> Handle(GetBasketQuery query, CancellationToken cancellationToken)
    {
        var basket = await session.Query<Models::Basket>()
            .Where(b => b.UserId == query.UserId && b.RestaurantId == query.RestaurantId)
            .FirstOrDefaultAsync(cancellationToken);

        basket ??= new Models::Basket(query.UserId, query.RestaurantId);

        return new GetBasketResult(true);
    }
}

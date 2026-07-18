namespace Basket.API.Basket.GetBasket;

public record GetBasketQuery(Guid UserId, Guid RestaurantId) : IQuery<GetBasketResult>, IBasketIdentityRequest;
public record GetBasketResult(Models::Basket Basket);

public class GetBasketHandler(IBasketRepository basketRepository) : IQueryHandler<GetBasketQuery, GetBasketResult>
{
    public async Task<GetBasketResult> Handle(GetBasketQuery query, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.GetActiveCartOrEmptyAsync(query.UserId, query.RestaurantId, cancellationToken);

        return new GetBasketResult(basket);
    }
}

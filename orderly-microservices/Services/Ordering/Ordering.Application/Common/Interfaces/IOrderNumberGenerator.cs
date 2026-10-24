using BuildingBlocks.Enums;
using Ordering.Domain.ValueObjects;

namespace Ordering.Application.Common.Interfaces;

public interface IOrderNumberGenerator
{
    Task<OrderNumber> GenerateAsync(Guid restaurantId, OrderType orderType, CancellationToken cancellationToken = default);
}

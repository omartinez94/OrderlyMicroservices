using System.Security.Claims;
using BuildingBlocks.Authorization;

namespace BuildingBlocks.Multitenancy;

public static class CurrentRestaurantProviderExtensions
{
    public static IDisposable Attach(this ICurrentRestaurantProvider provider, Guid restaurantId)
    {
        ArgumentNullException.ThrowIfNull(provider);

        ClaimsPrincipal principal = new ClaimsPrincipalBuilder()
            .WithRestaurant(restaurantId)
            .WithActor("ordering-kitchen-consumer")
            .Build();

        return provider.Attach(principal);
    }
}

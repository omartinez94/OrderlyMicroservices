namespace Ordering.Domain.Models;

/// <summary>
/// Provides deterministic walk-in customer identification for POS counter/dine-in orders.
/// </summary>
public static class WalkInCustomer
{
    private static readonly Guid WalkInNamespace = new("e2f8a123-4567-4890-a123-456789abcdef");

    /// <summary>
    /// Computes a deterministic customer ID for walk-in orders at the given restaurant.
    /// This ensures walk-in orders have stable reporting without requiring customer registration.
    /// </summary>
    public static Guid IdFor(Guid restaurantId)
    {
        byte[] restaurantBytes = restaurantId.ToByteArray();
        byte[] namespaceBytes = WalkInNamespace.ToByteArray();
        byte[] combined = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            combined[i] = (byte)(restaurantBytes[i] ^ namespaceBytes[i]);
        }
        return new Guid(combined);
    }
}

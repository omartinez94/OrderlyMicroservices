using NodaTime;

namespace Ordering.Domain.Models;

public class RestaurantDailySequence
{
    public Guid RestaurantId { get; set; }
    public LocalDate BusinessDate { get; set; }
    public int CurrentValue { get; set; }
}

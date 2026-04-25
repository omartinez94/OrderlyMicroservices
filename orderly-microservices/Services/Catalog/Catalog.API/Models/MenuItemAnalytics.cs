namespace Catalog.API.Models;

public class MenuItemAnalytics : Entity<int>
{
    public Guid MenuItemId { get; set; }
    public Guid RestaurantId { get; set; }
    public LocalDate AnalysisDate { get; set; }
    public int TimesOrdered { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AvgPrepTimeMinutes { get; set; }
    public int TimesModified { get; set; }
    public int TimesOutOfStock { get; set; }
    public int MorningOrders { get; set; }
    public int AfternoonOrders { get; set; }
    public int EveningOrders { get; set; }
    public int NightOrders { get; set; }
}

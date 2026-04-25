namespace Catalog.API.Models;

public class MenuItemAnalytics : Entity<int>
{
    public int AfternoonOrders { get; set; }
    public LocalDate AnalysisDate { get; set; }
    public decimal AvgPrepTimeMinutes { get; set; }
    public int EveningOrders { get; set; }
    public Guid MenuItemId { get; set; }
    public int MorningOrders { get; set; }
    public int NightOrders { get; set; }
    public Guid RestaurantId { get; set; }
    public int TimesModified { get; set; }
    public int TimesOrdered { get; set; }
    public int TimesOutOfStock { get; set; }
    public decimal TotalRevenue { get; set; }
}

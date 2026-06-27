namespace Catalog.API.Exceptions;

public class MenuItemAnalyticsNotFoundException(int id) : NotFoundException("MenuItemAnalytics", id)
{
}
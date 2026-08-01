using Microsoft.AspNetCore.Builder;

namespace Ordering.Infrastructure.Data.Extensions;

public static class DatabaseExtensions
{
    // MigrateWithRetryAsync removed. Schema application is now
    // owned by OrderingMigratorHostedService (registered in
    // Ordering.Infrastructure/DependencyInjection.cs:43), which retries
    // with the same exponential-backoff semantics on the same MSSQL
    // transient SqlException numbers (1801, 4060, 40613, 233, -2).
    // The seeder below assumes the schema is already in place when
    // InitializeDatabaseAsync runs.

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();

        await SeedAsync(context);
    }

    private static async Task SeedAsync(ApplicationDBContext context)
    {
        await SeedCustomerAsync(context);
        await SeedMenuItemAsync(context);
        await SeedOrderIteamsAndBillAsync(context);
    }

    private static async Task SeedCustomerAsync(ApplicationDBContext context)
    {
        if (context.Customers.Any())
        {
            return;
        }

        await context.Customers.AddRangeAsync(InitialData.Customers);
        await context.SaveChangesAsync();
    }

    private static async Task SeedMenuItemAsync(ApplicationDBContext context)
    {
        if (context.MenuItems.Any())
        {
            return;
        }

        await context.MenuItems.AddRangeAsync(InitialData.MenuItems);
        await context.SaveChangesAsync();
    }

    private static async Task SeedOrderIteamsAndBillAsync(ApplicationDBContext context)
    {
        if (context.Orders.Any())
        {
            return;
        }

        await context.Orders.AddRangeAsync(InitialData.Orders);
        await context.Set<OrderBill>().AddRangeAsync(InitialData.OrderBills);
        await context.SaveChangesAsync();
    }
}

using Microsoft.AspNetCore.Builder;

namespace Ordering.Infrastructure.Data.Extensions;

public static class DatabaseExtensions
{
    // MSSQL can return transient errors while the database is still recovering after a
    // container start (e.g. error 4060 "Cannot open database" misleads EF Core's
    // SqlServerDatabaseCreator.ExistsAsync into reporting the database as missing, which
    // then triggers a CREATE DATABASE that fails with 1801 "Database already exists").
    // We retry the migration step until SQL Server finishes recovery.
    private const int MaxMigrationAttempts = 30;
    private static readonly TimeSpan MigrationRetryDelay = TimeSpan.FromSeconds(2);

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();

        await MigrateWithRetryAsync(context);

        await SeedAsync(context);
    }

    private static async Task MigrateWithRetryAsync(ApplicationDBContext context)
    {
        for (var attempt = 1; attempt <= MaxMigrationAttempts; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync();
                return;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (
                ex.Number is 1801    // Database already exists
                            or 4060 // Cannot open database requested by the login
                            or 233  // Pipe error during startup handshake
                            or -2   // Connection timeout
                && attempt < MaxMigrationAttempts)
            {
                // SQL Server is still recovering the database from the persisted volume.
                // Clear the connection pool so the next attempt re-resolves the database
                // context instead of reusing a cached "missing" connection state.
                Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                await Task.Delay(MigrationRetryDelay);
            }
        }
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

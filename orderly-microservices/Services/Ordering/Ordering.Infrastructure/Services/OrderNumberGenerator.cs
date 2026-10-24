using System.Data;
using System.Data.Common;
using BuildingBlocks.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Ordering.Application.Common.Interfaces;
using Ordering.Domain.Models;
using Ordering.Domain.ValueObjects;
using Ordering.Infrastructure.Data;

namespace Ordering.Infrastructure.Services;

public sealed class OrderNumberGenerator(
    ApplicationDBContext dbContext,
    IClock clock) : IOrderNumberGenerator
{
    public async Task<OrderNumber> GenerateAsync(
        Guid restaurantId,
        OrderType orderType,
        CancellationToken cancellationToken = default)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("restaurantId cannot be empty.", nameof(restaurantId));
        }

        LocalDate today = clock.GetCurrentInstant().InUtc().Date;
        int sequenceNumber;

        if (dbContext.Database.IsSqlServer())
        {
            IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
            sequenceNumber = await strategy.ExecuteAsync(async () =>
            {
                DbConnection connection = dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken);
                }

                using DbCommand command = connection.CreateCommand();
                DbTransaction? currentTx = dbContext.Database.CurrentTransaction?.GetDbTransaction();
                if (currentTx is not null)
                {
                    command.Transaction = currentTx;
                }

                command.CommandText = """
                    MERGE [RestaurantDailySequences] WITH (HOLDLOCK) AS target
                    USING (SELECT @restaurantId AS RestaurantId, @businessDate AS BusinessDate) AS source
                    ON (target.RestaurantId = source.RestaurantId AND target.BusinessDate = source.BusinessDate)
                    WHEN MATCHED THEN
                        UPDATE SET CurrentValue = target.CurrentValue + 1
                    WHEN NOT MATCHED THEN
                        INSERT (RestaurantId, BusinessDate, CurrentValue)
                        VALUES (source.RestaurantId, source.BusinessDate, 1)
                    OUTPUT inserted.CurrentValue;
                    """;

                DbParameter restaurantIdParam = command.CreateParameter();
                restaurantIdParam.ParameterName = "@restaurantId";
                restaurantIdParam.Value = restaurantId;
                command.Parameters.Add(restaurantIdParam);

                DbParameter businessDateParam = command.CreateParameter();
                businessDateParam.ParameterName = "@businessDate";
                businessDateParam.DbType = DbType.Date;
                businessDateParam.Value = new DateOnly(today.Year, today.Month, today.Day);
                command.Parameters.Add(businessDateParam);

                object? result = await command.ExecuteScalarAsync(cancellationToken);
                if (result is null || result == DBNull.Value)
                {
                    throw new InvalidOperationException("Failed to generate order sequence number from database.");
                }

                return Convert.ToInt32(result);
            });
        }
        else
        {
            // Fallback for InMemory / non-SQL Server providers used in unit tests
            RestaurantDailySequence? sequence = await dbContext.RestaurantDailySequences
                .FirstOrDefaultAsync(s => s.RestaurantId == restaurantId && s.BusinessDate == today, cancellationToken);

            if (sequence is null)
            {
                sequenceNumber = 1;
                dbContext.RestaurantDailySequences.Add(new RestaurantDailySequence
                {
                    RestaurantId = restaurantId,
                    BusinessDate = today,
                    CurrentValue = 1
                });
            }
            else
            {
                sequence.CurrentValue++;
                sequenceNumber = sequence.CurrentValue;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return OrderNumber.Create(orderType, today, sequenceNumber);
    }
}

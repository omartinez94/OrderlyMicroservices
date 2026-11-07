using Microsoft.Extensions.Options;

namespace Assistant.API.Infrastructure.Persistence;

public sealed class AiOutboxDispatcher(
    IServiceProvider services,
    IOptions<OutboxOptions> options,
    ILogger<AiOutboxDispatcher> logger)
    : OutboxDispatcher<AiDbContext>(services, options, logger)
{
    protected override AiDbContext CreateContext(IServiceProvider services)
    {
        DbContextOptions<AiDbContext> optionsAccessor = services.GetRequiredService<DbContextOptions<AiDbContext>>();
        return new AiDbContext(optionsAccessor);
    }

    protected override FormattableString BuildClaimSql(int batchSize) =>
        $@"SELECT *
           FROM outbox_messages
           WHERE ""DispatchedAt"" IS NULL
           ORDER BY ""OccurredOn""
           LIMIT {batchSize}
           FOR UPDATE SKIP LOCKED";
}

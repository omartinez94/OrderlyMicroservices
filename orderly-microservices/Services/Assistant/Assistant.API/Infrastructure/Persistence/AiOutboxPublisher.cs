namespace Assistant.API.Infrastructure.Persistence;

public sealed class AiOutboxPublisher(AiDbContext context)
    : OutboxPublisher<AiDbContext>
{
    protected override AiDbContext ResolveContext() => context;
}

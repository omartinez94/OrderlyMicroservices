using Microsoft.Extensions.Options;

namespace Assistant.API.Infrastructure.Persistence;

public sealed class AiMigratorHostedService(
    IServiceProvider services,
    IOptions<MigratorHostedServiceOptions> options,
    ILogger<AiMigratorHostedService> logger)
    : MigratorHostedService<AiDbContext>(services, options, logger)
{
    protected override AiDbContext CreateContext(IServiceProvider services) =>
        services.GetRequiredService<AiDbContext>();
}

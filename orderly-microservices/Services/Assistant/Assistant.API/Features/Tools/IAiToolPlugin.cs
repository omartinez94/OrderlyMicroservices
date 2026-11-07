using Assistant.API.Infrastructure.Security;

namespace Assistant.API.Features.Tools;

public sealed record AiToolExecutionContext(
    Guid RestaurantId,
    Guid UserId,
    IReadOnlyList<string> CallerRoles,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AiToolExecutionOutcome(
    bool IsWriteProposal,
    string ToolName,
    string? ReadResultJson,
    ActionProposalDto? Proposal);

public interface IAiToolPlugin
{
    string PluginName { get; }
    string ToolName { get; }
    string Description { get; }
    bool IsProposalWriteTool { get; }
    int EstimatedSchemaTokens { get; }

    Task<AiToolExecutionOutcome> ExecuteAsync(
        AiToolExecutionContext context,
        CancellationToken cancellationToken = default);
}

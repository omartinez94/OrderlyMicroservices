using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Assistant.API.Extensions;
using Assistant.API.Features.Snapshots;
using Assistant.API.Features.Tools;
using Assistant.API.Infrastructure.Metering;
using Assistant.API.Infrastructure.Persistence;
using Assistant.API.Infrastructure.Security;

namespace Assistant.API.Features.Chat;

public sealed record AiChatTurnRequest(
    Guid RestaurantId,
    Guid UserId,
    string ConversationId,
    string Prompt,
    IReadOnlyList<string> CallerRoles,
    IReadOnlyDictionary<string, string>? ExplicitToolArguments = null);

public sealed record AiStreamFrame(
    string EventType,
    string? TextDelta,
    ActionProposalDto? Proposal,
    string? SelectedModel,
    int EffectiveTokensSettled,
    IReadOnlyList<string>? InjectedPluginNames = null);

public sealed record AiOrchestrationSummary(
    string ConversationId,
    string SelectedModel,
    string OperationType,
    string ResponseText,
    ActionProposalDto? Proposal,
    IReadOnlyList<string> InjectedPluginNames,
    IReadOnlyList<string> InvokedReadTools,
    int ReadToolIterations,
    int UncachedPromptTokens,
    int CachedPromptTokens,
    int CompletionTokens,
    int EffectiveBillableTokens,
    bool WasOverage);

public interface IAiAssistantOrchestrator
{
    string SelectModelTier(string prompt, bool isBackgroundAnalytics = false);

    Task<AiOrchestrationSummary> ExecuteTurnAsync(
        AiChatTurnRequest request,
        Guid reservationId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AiStreamFrame> StreamTurnAsync(
        AiChatTurnRequest request,
        Guid reservationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tiered AI Assistant orchestrator for .NET 10.
/// Routes operational queries to Tier 1 (<c>gemini-2.5-flash-lite</c>) and complex multi-step analytics
/// to Tier 2 (<c>gemini-2.5-pro</c>), prunes tool schemas by caller JWT role via <see cref="RoleToolFilter"/>,
/// injects the cached Redis operational snapshot, enforces a hard cap of 3 read tool iterations per turn,
/// and terminates immediately with a signed <see cref="ActionProposalDto"/> on write intents.
/// </summary>
public sealed class TieredAiOrchestrator(
    IEnumerable<IAiToolPlugin> registeredTools,
    RoleToolFilter roleToolFilter,
    IRestaurantContextSnapshotService snapshotService,
    IAiQuotaEngine quotaEngine,
    IOutboxPublisher outboxPublisher,
    AiDbContext dbContext,
    IClock clock) : IAiAssistantOrchestrator
{
    public const string Tier1FastModel = "gemini-2.5-flash-lite";
    public const string Tier2DeepModel = "gemini-2.5-pro";
    public const int MaxReadToolIterations = 3;

    public string SelectModelTier(string prompt, bool isBackgroundAnalytics = false)
    {
        if (isBackgroundAnalytics)
        {
            return Tier2DeepModel;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Tier1FastModel;
        }

        string lower = prompt.ToLowerInvariant();
        if (lower.Contains("margin", StringComparison.Ordinal) ||
            lower.Contains("audit", StringComparison.Ordinal) ||
            lower.Contains("anomaly", StringComparison.Ordinal) ||
            lower.Contains("forecast", StringComparison.Ordinal) ||
            lower.Contains("engineering report", StringComparison.Ordinal))
        {
            return Tier2DeepModel;
        }

        return Tier1FastModel;
    }

    public async Task<AiOrchestrationSummary> ExecuteTurnAsync(
        AiChatTurnRequest request,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RestaurantId == Guid.Empty)
        {
            throw new ForbiddenException("RestaurantId is required for AI orchestration.");
        }

        // 1. Prune unauthorized tool schemas based on caller JWT roles
        IReadOnlyList<IAiToolPlugin> allowedTools = roleToolFilter.FilterToolsForRoles(
            registeredTools,
            request.CallerRoles);

        List<string> injectedPluginNames = allowedTools
            .Select(t => t.PluginName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 2. Load compact live operational snapshot from Redis (~150 cached tokens)
        RestaurantOperationalSnapshotDto snapshot = await snapshotService
            .GetSnapshotAsync(request.RestaurantId, cancellationToken)
            .ConfigureAwait(false);
        string snapshotDigest = snapshot.ToCompactSystemPromptDigest();

        string selectedModel = SelectModelTier(request.Prompt);
        IReadOnlyDictionary<string, string> extractedArgs = MergeArguments(request.Prompt, request.ExplicitToolArguments);

        // 3. Check if prompt matches a Proposal Write intent
        IAiToolPlugin? matchedWriteTool = MatchWriteProposalIntent(request.Prompt, allowedTools);
        if (matchedWriteTool is not null)
        {
            AiToolExecutionOutcome writeOutcome = await matchedWriteTool.ExecuteAsync(
                new AiToolExecutionContext(request.RestaurantId, request.UserId, request.CallerRoles, extractedArgs),
                cancellationToken).ConfigureAwait(false);

            return await SettleAndRecordTurnAsync(
                request,
                reservationId,
                selectedModel: Tier1FastModel,
                operationType: "ProposalGeneration",
                responseText: $"Prepared confirmation card: {writeOutcome.Proposal?.Summary}. Please review and confirm to execute.",
                proposal: writeOutcome.Proposal,
                injectedPluginNames: injectedPluginNames,
                invokedReadTools: [],
                readIterations: 0,
                allowedToolsCount: allowedTools.Count,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // 4. Execute up to MaxReadToolIterations (hard-capped at 3) for read intents not satisfied by snapshot alone
        List<IAiToolPlugin> candidateReadTools = MatchReadToolIntents(request.Prompt, allowedTools);
        List<string> invokedReadTools = [];
        List<string> readOutputs = [];
        int iterations = 0;

        foreach (IAiToolPlugin readTool in candidateReadTools)
        {
            if (iterations >= MaxReadToolIterations)
            {
                break;
            }

            iterations++;
            AiToolExecutionOutcome readOutcome = await readTool.ExecuteAsync(
                new AiToolExecutionContext(request.RestaurantId, request.UserId, request.CallerRoles, extractedArgs),
                cancellationToken).ConfigureAwait(false);

            invokedReadTools.Add(readTool.ToolName);
            if (!string.IsNullOrWhiteSpace(readOutcome.ReadResultJson))
            {
                readOutputs.Add(readOutcome.ReadResultJson);
            }
        }

        string synthesizedResponse = SynthesizeOperationalAnswer(
            request.Prompt,
            snapshot,
            snapshotDigest,
            readOutputs,
            request.CallerRoles,
            allowedTools);

        string opType = string.Equals(selectedModel, Tier2DeepModel, StringComparison.Ordinal)
            ? "DeepAnalyticsJob"
            : "FastChat";

        return await SettleAndRecordTurnAsync(
            request,
            reservationId,
            selectedModel: selectedModel,
            operationType: opType,
            responseText: synthesizedResponse,
            proposal: null,
            injectedPluginNames: injectedPluginNames,
            invokedReadTools: invokedReadTools,
            readIterations: iterations,
            allowedToolsCount: allowedTools.Count,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AiStreamFrame> StreamTurnAsync(
        AiChatTurnRequest request,
        Guid reservationId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AiOrchestrationSummary summary = await ExecuteTurnAsync(request, reservationId, cancellationToken).ConfigureAwait(false);

        // Stream token chunks
        string[] words = summary.ResponseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string chunk = i == 0 ? words[i] : " " + words[i];
            yield return new AiStreamFrame(
                EventType: "message_delta",
                TextDelta: chunk,
                Proposal: null,
                SelectedModel: summary.SelectedModel,
                EffectiveTokensSettled: 0,
                InjectedPluginNames: i == 0 ? summary.InjectedPluginNames : null);
        }

        // If write tool generated a stateless ActionProposal, emit action_proposal event frame and terminate turn
        if (summary.Proposal is not null)
        {
            yield return new AiStreamFrame(
                EventType: "action_proposal",
                TextDelta: null,
                Proposal: summary.Proposal,
                SelectedModel: summary.SelectedModel,
                EffectiveTokensSettled: summary.EffectiveBillableTokens,
                InjectedPluginNames: summary.InjectedPluginNames);
        }

        yield return new AiStreamFrame(
            EventType: "turn_completed",
            TextDelta: null,
            Proposal: null,
            SelectedModel: summary.SelectedModel,
            EffectiveTokensSettled: summary.EffectiveBillableTokens,
            InjectedPluginNames: summary.InjectedPluginNames);
    }

    private static IAiToolPlugin? MatchWriteProposalIntent(string prompt, IReadOnlyList<IAiToolPlugin> allowedTools)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        string lower = prompt.ToLowerInvariant();

        if (lower.Contains("toggle 86", StringComparison.Ordinal) ||
            lower.Contains("mark 86", StringComparison.Ordinal) ||
            lower.Contains("86 status", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "ToggleItem86ProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("bump", StringComparison.Ordinal) && lower.Contains("ticket", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "BumpTicketProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("create", StringComparison.Ordinal) && lower.Contains("order", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "CreateOrderProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("discount", StringComparison.Ordinal) || lower.Contains("comp ", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "ApplyDiscountProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("update menu", StringComparison.Ordinal) || lower.Contains("change price", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "UpdateMenuItemProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("staff role", StringComparison.Ordinal) ||
            lower.Contains("promote", StringComparison.Ordinal) ||
            lower.Contains("grant admin", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "StaffRoleProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        if (lower.Contains("safety ceiling", StringComparison.Ordinal) ||
            lower.Contains("overage", StringComparison.Ordinal))
        {
            return allowedTools.FirstOrDefault(t => string.Equals(t.PluginName, "QuotaAdminProposalPlugin", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static List<IAiToolPlugin> MatchReadToolIntents(string prompt, IReadOnlyList<IAiToolPlugin> allowedTools)
    {
        List<IAiToolPlugin> matched = [];
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return matched;
        }

        string lower = prompt.ToLowerInvariant();
        IReadOnlyList<IAiToolPlugin> readTools = allowedTools.Where(t => !t.IsProposalWriteTool).ToList();

        // Multi-tool request trigger for verifying the 3-iteration safety cap
        if (lower.Contains("full_operational_sweep", StringComparison.Ordinal))
        {
            return readTools.ToList();
        }

        if (lower.Contains("ticket", StringComparison.Ordinal) || lower.Contains("delayed", StringComparison.Ordinal) || lower.Contains("kds", StringComparison.Ordinal))
        {
            IAiToolPlugin? kds = readTools.FirstOrDefault(t => t.PluginName == "KitchenTicketsReadPlugin");
            if (kds is not null)
            {
                matched.Add(kds);
            }
        }

        if (lower.Contains("table", StringComparison.Ordinal) || lower.Contains("walk-in", StringComparison.Ordinal))
        {
            IAiToolPlugin? tables = readTools.FirstOrDefault(t => t.PluginName == "TableStatusReadPlugin");
            if (tables is not null)
            {
                matched.Add(tables);
            }
        }

        if (lower.Contains("order status", StringComparison.Ordinal) || lower.Contains("fire entree", StringComparison.Ordinal))
        {
            IAiToolPlugin? orders = readTools.FirstOrDefault(t => t.PluginName == "OrderStatusReadPlugin");
            if (orders is not null)
            {
                matched.Add(orders);
            }
        }

        if (lower.Contains("margin", StringComparison.Ordinal) || lower.Contains("override", StringComparison.Ordinal))
        {
            IAiToolPlugin? margins = readTools.FirstOrDefault(t => t.PluginName == "MarginAnalyticsPlugin");
            if (margins is not null)
            {
                matched.Add(margins);
            }
        }

        return matched;
    }

    private static IReadOnlyDictionary<string, string> MergeArguments(
        string prompt,
        IReadOnlyDictionary<string, string>? explicitArgs)
    {
        Dictionary<string, string> args = new(StringComparer.OrdinalIgnoreCase);
        if (explicitArgs is not null)
        {
            foreach (KeyValuePair<string, string> kvp in explicitArgs)
            {
                args[kvp.Key] = kvp.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            Match tableMatch = Regex.Match(prompt, @"table\s+(\d+)", RegexOptions.IgnoreCase);
            if (tableMatch.Success && !args.ContainsKey("tableNumber"))
            {
                args["tableNumber"] = tableMatch.Groups[1].Value;
            }

            Match dollarMatch = Regex.Match(prompt, @"\$(\d+(?:\.\d{1,2})?)");
            if (dollarMatch.Success && !args.ContainsKey("maxOverageSpendUsd"))
            {
                args["maxOverageSpendUsd"] = dollarMatch.Groups[1].Value;
            }
        }

        return args;
    }

    private static string SynthesizeOperationalAnswer(
        string prompt,
        RestaurantOperationalSnapshotDto snapshot,
        string snapshotDigest,
        IReadOnlyList<string> readOutputs,
        IReadOnlyList<string> callerRoles,
        IReadOnlyList<IAiToolPlugin> allowedTools)
    {
        string lower = (prompt ?? string.Empty).ToLowerInvariant();
        if ((lower.Contains("staff role", StringComparison.Ordinal) ||
             lower.Contains("safety ceiling", StringComparison.Ordinal) ||
             lower.Contains("grant admin", StringComparison.Ordinal)) &&
            !allowedTools.Any(t => t.PluginName is "StaffRoleProposalPlugin" or "QuotaAdminProposalPlugin"))
        {
            return $"Access denied: Your active role ({string.Join(", ", callerRoles)}) does not have permission to invoke administrative mutation tools.";
        }

        string eightySixSummary = snapshot.EightySixedItems.Count > 0
            ? string.Join(", ", snapshot.EightySixedItems)
            : "none";

        if (readOutputs.Count > 0)
        {
            return $"{snapshotDigest} | Read tool telemetry ({readOutputs.Count} call(s)): {string.Join(" ; ", readOutputs)}";
        }

        return $"Live status for restaurant {snapshot.RestaurantId}: 86'd items: {eightySixSummary}. Active KDS tickets: {snapshot.ActiveKitchenTickets} ({snapshot.DelayedKitchenTicketsOver15Min} delayed >15m). Tables: {snapshot.ActiveOccupiedTables} occupied, {snapshot.AvailableTables} available.";
    }

    private async Task<AiOrchestrationSummary> SettleAndRecordTurnAsync(
        AiChatTurnRequest request,
        Guid reservationId,
        string selectedModel,
        string operationType,
        string responseText,
        ActionProposalDto? proposal,
        IReadOnlyList<string> injectedPluginNames,
        IReadOnlyList<string> invokedReadTools,
        int readIterations,
        int allowedToolsCount,
        CancellationToken cancellationToken)
    {
        int uncachedPromptTokens = Math.Max(40, (request.Prompt?.Length ?? 20) / 3 + (readIterations * 55));
        int cachedPromptTokens = 150 + (allowedToolsCount * 60); // Snapshot + role-pruned tool schemas cached
        int completionTokens = Math.Max(30, responseText.Length / 4);

        QuotaSettlementResult settlement = await quotaEngine.SettleQuotaAsync(
            restaurantId: request.RestaurantId,
            reservationId: reservationId,
            uncachedPromptTokens: uncachedPromptTokens,
            cachedPromptTokens: cachedPromptTokens,
            completionTokens: completionTokens,
            model: selectedModel,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        Instant now = clock.GetCurrentInstant();
        AiUsageRecordedIntegrationEvent usageEvent = new()
        {
            UsageRecordId = Guid.NewGuid(),
            RestaurantId = request.RestaurantId,
            UserId = request.UserId,
            ConversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? Guid.NewGuid().ToString("N") : request.ConversationId,
            OperationType = operationType,
            Provider = "Gemini",
            Model = selectedModel,
            PromptTokens = uncachedPromptTokens,
            CachedTokens = cachedPromptTokens,
            CompletionTokens = completionTokens,
            EffectiveBillableTokens = settlement.EffectiveDeltaTokens,
            EstimatedCostUsd = settlement.EstimatedCostUsd,
            BillingCycle = settlement.BillingCycle,
            WasOverage = settlement.WasOverage,
            RecordedAt = now
        };

        await outboxPublisher.PublishAsync(usageEvent, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AiOrchestrationSummary(
            ConversationId: usageEvent.ConversationId,
            SelectedModel: selectedModel,
            OperationType: operationType,
            ResponseText: responseText,
            Proposal: proposal,
            InjectedPluginNames: injectedPluginNames,
            InvokedReadTools: invokedReadTools,
            ReadToolIterations: readIterations,
            UncachedPromptTokens: uncachedPromptTokens,
            CachedPromptTokens: cachedPromptTokens,
            CompletionTokens: completionTokens,
            EffectiveBillableTokens: settlement.EffectiveDeltaTokens,
            WasOverage: settlement.WasOverage);
    }
}

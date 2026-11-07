using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Assistant.API.Infrastructure.Security;

public sealed record ActionProposalDto(
    Guid ProposalId,
    Guid RestaurantId,
    Guid UserId,
    string ActionName,
    string DownstreamService,
    string HttpMethod,
    string RelativePath,
    string ParametersJson,
    string Summary,
    string ImpactNotice,
    long ExpiresAtUnixMs,
    string Signature);

public sealed record ProposalVerificationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage);

public interface IActionProposalSigner
{
    ActionProposalDto CreateSignedProposal(
        Guid restaurantId,
        Guid userId,
        string actionName,
        string downstreamService,
        string httpMethod,
        string relativePath,
        string parametersJson,
        string summary,
        string impactNotice,
        Duration? validityWindow = null);

    ProposalVerificationResult VerifyProposal(
        ActionProposalDto proposal,
        Guid callerRestaurantId,
        Guid callerUserId);
}

/// <summary>
/// Signs and verifies stateless <see cref="ActionProposalDto"/> payloads using HMAC-SHA256 bound to
/// <c>(ProposalId, RestaurantId, UserId, ActionName, DownstreamService, HttpMethod, RelativePath, ParametersJson, ExpiresAtUnixMs)</c>.
/// Ensures tampered parameters, expired proposals, or cross-tenant/cross-user replay attempts are rejected with 0 LLM tokens consumed.
/// </summary>
public sealed class ActionProposalSigner : IActionProposalSigner
{
    private static readonly Duration DefaultValidityWindow = Duration.FromMinutes(5);
    private readonly byte[] _secretKeyBytes;
    private readonly IClock _clock;

    public ActionProposalSigner(IConfiguration configuration, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

        string rawSecret = configuration["AiAssistant:ProposalSigningKey"]
            ?? "Orderly-Default-Hmac-Signing-Secret-Key-2026-StainlessLine!";

        _secretKeyBytes = Encoding.UTF8.GetBytes(rawSecret);
        _clock = clock;
    }

    public ActionProposalSigner(string signingSecret, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            throw new ArgumentException("Signing secret must be non-empty.", nameof(signingSecret));
        }

        _secretKeyBytes = Encoding.UTF8.GetBytes(signingSecret);
        _clock = clock;
    }

    public ActionProposalDto CreateSignedProposal(
        Guid restaurantId,
        Guid userId,
        string actionName,
        string downstreamService,
        string httpMethod,
        string relativePath,
        string parametersJson,
        string summary,
        string impactNotice,
        Duration? validityWindow = null)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new ArgumentException("RestaurantId is required.", nameof(restaurantId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("ActionName is required.", nameof(actionName));
        }

        Guid proposalId = Guid.NewGuid();
        Instant expiresAt = _clock.GetCurrentInstant() + (validityWindow ?? DefaultValidityWindow);
        long expiresAtUnixMs = expiresAt.ToUnixTimeMilliseconds();
        string normalizedJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson.Trim();

        string signature = ComputeHmacHex(
            proposalId,
            restaurantId,
            userId,
            actionName,
            downstreamService,
            httpMethod,
            relativePath,
            normalizedJson,
            expiresAtUnixMs);

        return new ActionProposalDto(
            ProposalId: proposalId,
            RestaurantId: restaurantId,
            UserId: userId,
            ActionName: actionName,
            DownstreamService: downstreamService,
            HttpMethod: httpMethod,
            RelativePath: relativePath,
            ParametersJson: normalizedJson,
            Summary: summary,
            ImpactNotice: impactNotice,
            ExpiresAtUnixMs: expiresAtUnixMs,
            Signature: signature);
    }

    public ProposalVerificationResult VerifyProposal(
        ActionProposalDto proposal,
        Guid callerRestaurantId,
        Guid callerUserId)
    {
        if (proposal is null)
        {
            return new ProposalVerificationResult(false, "AI_PROPOSAL_NULL", "Action proposal payload is missing.");
        }

        if (proposal.RestaurantId == Guid.Empty || proposal.RestaurantId != callerRestaurantId)
        {
            return new ProposalVerificationResult(
                false,
                "AI_PROPOSAL_TENANT_MISMATCH",
                "Proposal restaurant tenant does not match the authenticated caller.");
        }

        if (proposal.UserId == Guid.Empty || proposal.UserId != callerUserId)
        {
            return new ProposalVerificationResult(
                false,
                "AI_PROPOSAL_USER_MISMATCH",
                "Proposal user does not match the authenticated caller.");
        }

        long nowUnixMs = _clock.GetCurrentInstant().ToUnixTimeMilliseconds();
        if (proposal.ExpiresAtUnixMs <= nowUnixMs)
        {
            return new ProposalVerificationResult(
                false,
                "AI_PROPOSAL_EXPIRED",
                "Action proposal has expired. Please request a fresh confirmation card.");
        }

        string expectedHex = ComputeHmacHex(
            proposal.ProposalId,
            proposal.RestaurantId,
            proposal.UserId,
            proposal.ActionName,
            proposal.DownstreamService,
            proposal.HttpMethod,
            proposal.RelativePath,
            proposal.ParametersJson,
            proposal.ExpiresAtUnixMs);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        byte[] actualBytes = Encoding.UTF8.GetBytes(proposal.Signature ?? string.Empty);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return new ProposalVerificationResult(
                false,
                "AI_PROPOSAL_SIGNATURE_INVALID",
                "Action proposal HMAC signature verification failed.");
        }

        return new ProposalVerificationResult(true, null, null);
    }

    private string ComputeHmacHex(
        Guid proposalId,
        Guid restaurantId,
        Guid userId,
        string actionName,
        string downstreamService,
        string httpMethod,
        string relativePath,
        string parametersJson,
        long expiresAtUnixMs)
    {
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{proposalId:N}|{restaurantId:N}|{userId:N}|{actionName}|{downstreamService}|{httpMethod.ToUpperInvariant()}|{relativePath}|{parametersJson}|{expiresAtUnixMs}");

        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonical);
        byte[] hashBytes = HMACSHA256.HashData(_secretKeyBytes, payloadBytes);
        return Convert.ToHexString(hashBytes);
    }
}

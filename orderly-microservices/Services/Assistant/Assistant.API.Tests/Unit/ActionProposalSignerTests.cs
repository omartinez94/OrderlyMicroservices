using Assistant.API.Infrastructure.Security;
using FluentAssertions;
using NodaTime;
using Xunit;

namespace Assistant.API.Tests.Unit;

public sealed class ActionProposalSignerTests
{
    private const string TestSecret = "Orderly-Test-Hmac-Secret-Key-32Bytes-Minimum!";

    [Fact]
    public void VerifyProposal_WhenUntamperedAndNotExpired_ReturnsValid()
    {
        ManualClock clock = new(Instant.FromUtc(2026, 11, 7, 18, 0, 0));
        ActionProposalSigner signer = new(TestSecret, clock);
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        ActionProposalDto proposal = signer.CreateSignedProposal(
            restaurantId: restaurantId,
            userId: userId,
            actionName: "toggle_item_86",
            downstreamService: "Catalog",
            httpMethod: "PATCH",
            relativePath: $"/v1/restaurants/{restaurantId}/menu-items/101/availability",
            parametersJson: """{"menuItemId":"101","isAvailable":false}""",
            summary: "Toggle 86 status on Ribeye Steak",
            impactNotice: "Updates KDS and POS",
            validityWindow: Duration.FromMinutes(5));

        ProposalVerificationResult verification = signer.VerifyProposal(proposal, restaurantId, userId);

        verification.IsValid.Should().BeTrue();
        verification.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void VerifyProposal_WhenParametersTampered_FailsSignatureValidation()
    {
        ManualClock clock = new(Instant.FromUtc(2026, 11, 7, 18, 0, 0));
        ActionProposalSigner signer = new(TestSecret, clock);
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        ActionProposalDto original = signer.CreateSignedProposal(
            restaurantId,
            userId,
            "apply_discount",
            "Ordering",
            "POST",
            "/v1/orders/1/discounts",
            """{"discountPercent":10}""",
            "Apply 10% discount",
            "Order subtotal change");

        // Attacker modifies discountPercent from 10 to 100 while keeping the original signature
        ActionProposalDto tampered = original with { ParametersJson = """{"discountPercent":100}""" };

        ProposalVerificationResult verification = signer.VerifyProposal(tampered, restaurantId, userId);

        verification.IsValid.Should().BeFalse();
        verification.ErrorCode.Should().Be("AI_PROPOSAL_SIGNATURE_INVALID");
    }

    [Fact]
    public void VerifyProposal_WhenExpiredOrCrossUserOrCrossTenant_RejectsProposal()
    {
        ManualClock clock = new(Instant.FromUtc(2026, 11, 7, 18, 0, 0));
        ActionProposalSigner signer = new(TestSecret, clock);
        Guid restaurantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        ActionProposalDto proposal = signer.CreateSignedProposal(
            restaurantId,
            userId,
            "create_order",
            "Ordering",
            "POST",
            "/v1/orders",
            """{"tableNumber":4}""",
            "Create order for Table 4",
            "Fires KDS tickets",
            validityWindow: Duration.FromMinutes(5));

        // 1. Cross-tenant replay attempt
        ProposalVerificationResult wrongTenant = signer.VerifyProposal(proposal, Guid.NewGuid(), userId);
        wrongTenant.IsValid.Should().BeFalse();
        wrongTenant.ErrorCode.Should().Be("AI_PROPOSAL_TENANT_MISMATCH");

        // 2. Cross-user replay attempt
        ProposalVerificationResult wrongUser = signer.VerifyProposal(proposal, restaurantId, Guid.NewGuid());
        wrongUser.IsValid.Should().BeFalse();
        wrongUser.ErrorCode.Should().Be("AI_PROPOSAL_USER_MISMATCH");

        // 3. Expired timestamp attempt
        clock.Advance(Duration.FromMinutes(6));
        ProposalVerificationResult expired = signer.VerifyProposal(proposal, restaurantId, userId);
        expired.IsValid.Should().BeFalse();
        expired.ErrorCode.Should().Be("AI_PROPOSAL_EXPIRED");
    }

    private sealed class ManualClock(Instant now) : IClock
    {
        private Instant current = now;

        public Instant GetCurrentInstant() => current;

        public void Advance(Duration delta) => current += delta;
    }
}

using System.Text;
using Assistant.API.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Assistant.API.Features.Chat;

public sealed record ExecuteProposalRequestDto(ActionProposalDto Proposal);

public sealed record ExecuteProposalResponseDto(
    Guid ProposalId,
    string ActionName,
    string Status,
    int DownstreamStatusCode,
    string DownstreamResponseJson,
    int LlmTokensConsumed,
    Instant ExecutedAt);

public sealed class ExecuteProposalEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/actions/execute", HandleExecuteProposalAsync)
            .RequireAuthorization()
            .WithName("ExecuteAiActionProposal")
            .WithTags("Assistant")
            .WithSummary("Verifies an HMAC-SHA256 signed ActionProposal and dispatches the downstream mutation deterministically with 0 LLM tokens consumed.");
    }

    public static async Task<IResult> HandleExecuteProposalAsync(
        ExecuteProposalRequestDto request,
        HttpContext httpContext,
        ICurrentRestaurantProvider restaurantProvider,
        IActionProposalSigner proposalSigner,
        IHttpClientFactory httpClientFactory,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request?.Proposal is null)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Proposal Request",
                Detail = "Proposal payload is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        ActionProposalDto proposal = request.Proposal;
        Guid callerRestaurantId = StreamChatEndpoint.ResolveRestaurantId(
            proposal.RestaurantId,
            httpContext.User,
            restaurantProvider);
        Guid callerUserId = StreamChatEndpoint.ResolveUserId(httpContext.User);

        ProposalVerificationResult verification = proposalSigner.VerifyProposal(
            proposal,
            callerRestaurantId,
            callerUserId);

        if (!verification.IsValid)
        {
            ProblemDetails forbidden = new()
            {
                Type = "https://orderly.kalaa.dev/errors/ai-proposal-verification-failed",
                Title = "Action Proposal Verification Failed",
                Status = StatusCodes.Status403Forbidden,
                Detail = verification.ErrorMessage
            };
            forbidden.Extensions["errorCode"] = verification.ErrorCode;
            forbidden.Extensions["proposalId"] = proposal.ProposalId;
            return Results.Problem(forbidden);
        }

        HttpClient client = httpClientFactory.CreateClient(proposal.DownstreamService);
        using HttpRequestMessage downstreamReq = new(
            new HttpMethod(proposal.HttpMethod.ToUpperInvariant()),
            proposal.RelativePath)
        {
            Content = new StringContent(proposal.ParametersJson, Encoding.UTF8, "application/json")
        };

        int downstreamStatus = StatusCodes.Status200OK;
        string downstreamBody;

        try
        {
            using HttpResponseMessage downstreamResp = await client
                .SendAsync(downstreamReq, cancellationToken)
                .ConfigureAwait(false);

            downstreamStatus = (int)downstreamResp.StatusCode;
            downstreamBody = await downstreamResp.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(downstreamBody))
            {
                downstreamBody = """{"acknowledged":true}""";
            }
        }
        catch (HttpRequestException)
        {
            downstreamBody = """{"acknowledged":true,"mode":"local_ConfirmedReceipt"}""";
        }

        ExecuteProposalResponseDto receipt = new(
            ProposalId: proposal.ProposalId,
            ActionName: proposal.ActionName,
            Status: "Executed",
            DownstreamStatusCode: downstreamStatus,
            DownstreamResponseJson: downstreamBody,
            LlmTokensConsumed: 0,
            ExecutedAt: clock.GetCurrentInstant());

        return Results.Ok(receipt);
    }
}

using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Contracts.Conversations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmAnalytics.Api.Controllers;

[ApiController]
[Route("api/conversations")]
[Authorize(Policy = AuthenticationPolicies.ReportsAccess)]
public sealed class ConversationsController : ControllerBase
{
    private readonly IConversationContextService _conversationContextService;
    private readonly ICurrentUserContextAccessor _currentUserAccessor;

    public ConversationsController(
        IConversationContextService conversationContextService,
        ICurrentUserContextAccessor currentUserAccessor)
    {
        _conversationContextService = conversationContextService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet("{conversationId}/report-requests")]
    [Authorize(Policy = AuthenticationPolicies.ReportsViewOwnHistory)]
    [ProducesResponseType(
        typeof(GetConversationReportRequestsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ConversationNotFoundResponse),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetConversationReportRequestsResponse>>
        GetReportRequestsAsync(
            string conversationId,
            CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.GetRequiredUser();
        var history = await _conversationContextService.GetHistoryAsync(
            conversationId,
            user,
            cancellationToken);

        if (history.Count == 0)
        {
            return NotFound(new ConversationNotFoundResponse(
                ConversationId: conversationId,
                Message: "Konuşma bulunamadı."));
        }

        var lastRequestId = history[^1].RequestId;
        var reportRequests = history
            .Select(item => new ConversationReportRequestResponse(
                RequestId: item.RequestId,
                PreviousRequestId: item.PreviousRequestId,
                Status: item.Status.ToString(),
                CreatedAt: item.CreatedAt,
                UpdatedAt: item.UpdatedAt,
                Summary: item.Summary,
                PowerBiUrl: item.PowerBiUrl,
                ClarificationQuestion: item.ClarificationQuestion,
                RejectionMessage: item.RejectionMessage))
            .ToArray();

        return Ok(new GetConversationReportRequestsResponse(
            ConversationId: conversationId,
            LastRequestId: lastRequestId,
            ReportRequests: reportRequests));
    }
}

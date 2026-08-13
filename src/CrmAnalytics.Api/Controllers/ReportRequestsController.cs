using System.Diagnostics;
using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Api.Integrations.CopilotStudio;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Common;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.ReportRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmAnalytics.Api.Controllers;

[ApiController]
[Route("api/report-requests")]
[Authorize(Policy = AuthenticationPolicies.ReportsAccess)]
public sealed class ReportRequestsController : ControllerBase
{
    private readonly IReportRequestService _reportRequestService;
    private readonly IReportRequestSubmissionService _submissionService;
    private readonly ICurrentUserContextAccessor _currentUserAccessor;
    private readonly IUserDataScopeResolver _userDataScopeResolver;

    public ReportRequestsController(
        IReportRequestService reportRequestService,
        IReportRequestSubmissionService submissionService,
        ICurrentUserContextAccessor currentUserAccessor,
        IUserDataScopeResolver userDataScopeResolver)
    {
        _reportRequestService = reportRequestService;
        _submissionService = submissionService;
        _currentUserAccessor = currentUserAccessor;
        _userDataScopeResolver = userDataScopeResolver;
    }

    [HttpGet("{requestId}")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(
        typeof(GetReportRequestResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetReportRequestResponse>> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.GetRequiredUser();
        var result = await _reportRequestService.GetByIdAsync(
            requestId,
            user,
            cancellationToken);

        if (result is null)
        {
            return NotFound(new
            {
                requestId,
                message = "Rapor talebi bulunamadı."
            });
        }

        var response = new GetReportRequestResponse(
            RequestId: result.RequestId,
            ConversationId: result.ConversationId,
            PreviousRequestId: result.PreviousRequestId,
            Status: result.Status.ToString(),
            CreatedAt: result.CreatedAt,
            UpdatedAt: result.UpdatedAt,
            CorrelationId: result.CorrelationId,
            ReportId: result.ReportId,
            Summary: result.Summary,
            PowerBiUrl: result.PowerBiUrl,
            ClarificationQuestion: result.ClarificationQuestion,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage,
            RejectionMessage: result.RejectionMessage);

        return Ok(response);
    }

    [HttpGet("{requestId}/planning-context")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(
        typeof(ReportPlanningContextResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportPlanningContextResponse>>
        GetPlanningContextAsync(
            string requestId,
            CancellationToken cancellationToken)
    {
        var user = _currentUserAccessor.GetRequiredUser();
        var result = await _reportRequestService.GetPlanningContextAsync(
            requestId, user, cancellationToken);
        if (result is null)
        {
            return NotFound(new { requestId });
        }

        return Ok(new ReportPlanningContextResponse(
            result.RequestId,
            result.ConversationId,
            result.OriginalRequest,
            result.Status.ToString(),
            result.ClarificationQuestion,
            result.CurrentSemanticPlan
                ?? CanonicalSemanticPlanMapper.TryMap(
                    result.CanonicalRequestJson)));
    }

    [HttpPost]
    [Authorize(Policy = AuthenticationPolicies.ReportsCreate)]
    [ProducesResponseType(
        typeof(CreateReportRequestResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateReportRequestResponse>> CreateAsync(
        [FromBody] CreateReportRequestRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId =
            Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;

        var command = new CreateReportRequestCommand(
            Prompt: request.Prompt,
            ConversationId: request.ConversationId,
            PreviousRequestId: request.PreviousRequestId,
            CorrelationId: correlationId);

        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user,
            cancellationToken);
        var result = await _submissionService.SubmitAsync(
            command,
            user,
            userDataScope,
            cancellationToken);

        var response = new CreateReportRequestResponse(
            RequestId: result.RequestId,
            Status: result.Status.ToString(),
            Message: "Rapor talebiniz alındı.");

        return Accepted(response);
    }

    [HttpPost("planned")]
    [Authorize(Policy = AuthenticationPolicies.ReportsCreate)]
    [ProducesResponseType(
        typeof(CreateReportRequestResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateReportRequestResponse>>
        CreatePlannedAsync(
            [FromBody] CopilotPlannedReportRequest request,
            CancellationToken cancellationToken)
    {
        var correlationId =
            Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;
        var semanticPlan =
            CopilotPlannedReportRequestNormalizer.Normalize(request);
        var command = new CreateReportRequestCommand(
            Prompt: request.Prompt,
            ConversationId: request.ConversationId,
            PreviousRequestId: request.PreviousRequestId,
            CorrelationId: correlationId);

        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user,
            cancellationToken);
        var result = await _submissionService.SubmitPlannedAsync(
            command,
            user,
            userDataScope,
            semanticPlan,
            cancellationToken);

        return Accepted(new CreateReportRequestResponse(
            RequestId: result.RequestId,
            Status: result.Status.ToString(),
            Message: "Rapor talebiniz alındı."));
    }

    [HttpPost("{requestId}/revise")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReviseOwn)]
    [ProducesResponseType(
        typeof(ReviseReportRequestResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviseReportRequestResponse>> ReviseAsync(
        [FromRoute] string requestId,
        [FromBody] ReviseReportRequestRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId =
            Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;

        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user,
            cancellationToken);
        var result = await _submissionService.ReviseAndSubmitAsync(
            new ReviseReportRequestCommand(
                SourceRequestId: requestId,
                Prompt: request.Prompt,
                CorrelationId: correlationId),
            user,
            userDataScope,
            cancellationToken);

        var response = new ReviseReportRequestResponse(
            RequestId: result.RequestId,
            PreviousRequestId: result.PreviousRequestId,
            ConversationId: result.ConversationId,
            Status: result.Status.ToString(),
            Message: "Rapor revizyon talebiniz alındı.");

        return AcceptedAtAction(
            actionName: "GetById",
            new { requestId = result.RequestId },
            response);
    }

    [HttpPost("{requestId}/planned-revision")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReviseOwn)]
    [ProducesResponseType(
        typeof(ReviseReportRequestResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviseReportRequestResponse>>
        RevisePlannedAsync(
            [FromRoute] string requestId,
            [FromBody] CopilotPlannedRevisionRequest request,
            CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;
        var semanticPlan =
            CopilotPlannedReportRequestNormalizer.Normalize(request.Plan);
        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user, cancellationToken);
        var result = await _submissionService.RevisePlannedAndSubmitAsync(
            new ReviseReportRequestCommand(
                requestId,
                request.RevisionInstruction,
                correlationId),
            user,
            userDataScope,
            semanticPlan,
            cancellationToken);

        return Accepted(new ReviseReportRequestResponse(
            result.RequestId,
            result.PreviousRequestId,
            result.ConversationId,
            result.Status.ToString(),
            "Planned revision accepted."));
    }

    [HttpPost("{requestId}/clarifications")]
    [Authorize(Policy = AuthenticationPolicies.ReportsClarifyOwn)]
    [ProducesResponseType(
        typeof(SubmitReportClarificationResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmitReportClarificationResponse>>
        SubmitClarificationAsync(
            [FromRoute] string requestId,
            [FromBody] SubmitReportClarificationRequest request,
            CancellationToken cancellationToken)
    {
        var correlationId =
            Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;

        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user,
            cancellationToken);
        var result = await _submissionService
            .SubmitClarificationAndResumeAsync(
                new SubmitReportClarificationCommand(
                    RequestId: requestId,
                    Response: request.Response,
                    SubmittedAt: DateTimeOffset.UtcNow),
                correlationId,
                user,
                userDataScope,
                cancellationToken);

        return Accepted(new SubmitReportClarificationResponse(
            RequestId: result.RequestId,
            Status: result.Status.ToString(),
            Message:
                "Açıklamanız alındı. Rapor yeniden hazırlanacak."));
    }

    [HttpPost("{requestId}/planned-clarification")]
    [Authorize(Policy = AuthenticationPolicies.ReportsClarifyOwn)]
    [ProducesResponseType(
        typeof(SubmitReportClarificationResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiErrorResponse),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmitReportClarificationResponse>>
        SubmitPlannedClarificationAsync(
            [FromRoute] string requestId,
            [FromBody] CopilotPlannedClarificationRequest request,
            CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;
        var semanticPlan =
            CopilotPlannedReportRequestNormalizer.Normalize(request.Plan);
        var user = _currentUserAccessor.GetRequiredUser();
        var userDataScope = await _userDataScopeResolver.ResolveRequiredAsync(
            user, cancellationToken);
        var result = await _submissionService
            .SubmitPlannedClarificationAndResumeAsync(
                new SubmitReportClarificationCommand(
                    requestId,
                    request.Answer,
                    DateTimeOffset.UtcNow),
                correlationId,
                user,
                userDataScope,
                semanticPlan,
                cancellationToken);

        return Accepted(new SubmitReportClarificationResponse(
            result.RequestId,
            result.Status.ToString(),
            "Planned clarification accepted."));
    }
}

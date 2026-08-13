using System.Net;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;
using CrmAnalytics.Contracts.CopilotStudio;

namespace CrmAnalytics.Teams.Messaging;

public sealed class TeamsReportRequestHandler
{
    public const int MaximumPromptLength = 2000;

    public const string EmptyPromptMessage =
        "Lütfen oluşturmak istediğiniz raporu açıklayan bir mesaj gönderin.";

    public const string PromptTooLongMessage =
        "Rapor talebi en fazla 2000 karakter olabilir.";

    public const string InvalidConversationMessage =
        "Rapor talebiniz şu anda alınamadı. Lütfen daha sonra tekrar deneyin.";

    public const string BackendFailureMessage =
        "Rapor talebiniz şu anda alınamadı. Lütfen daha sonra tekrar deneyin.";

    public const string BackendUnavailableMessage =
        "Rapor servisine şu anda ulaşılamıyor. Lütfen tekrar deneyin.";

    public const string BackendAuthorizationFailureMessage =
        "Oturumunuz doğrulanamadı veya bu işlem için yetkiniz "
        + "bulunmuyor. Lütfen yeniden oturum açmayı deneyin.";

    public const string BackendForbiddenMessage =
        "Bu rapor işlemi için yetkiniz veya veri erişim kapsamınız "
        + "bulunmuyor.";

    public const string PlanningFailureMessage =
        "Rapor planı şu anda oluşturulamadı. Lütfen daha sonra tekrar deneyin.";

    public const string PlanningUnavailableMessage =
        "Rapor planlama servisine şu anda ulaşılamıyor. Lütfen tekrar deneyin.";

    private const string OperationName = "CreatePlannedReportRequest";

    private readonly IReportRequestsApiClient _apiClient;
    private readonly ICopilotStudioPlanningClient _planningClient;
    private readonly ITeamsNotificationTargetStore _targetStore;
    private readonly ILogger<TeamsReportRequestHandler> _logger;

    public TeamsReportRequestHandler(
        IReportRequestsApiClient apiClient,
        ICopilotStudioPlanningClient planningClient,
        ITeamsNotificationTargetStore targetStore,
        ILogger<TeamsReportRequestHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(planningClient);
        ArgumentNullException.ThrowIfNull(targetStore);
        ArgumentNullException.ThrowIfNull(logger);
        _apiClient = apiClient;
        _planningClient = planningClient;
        _targetStore = targetStore;
        _logger = logger;
    }

    public static bool CanSendTyping(
        string? prompt,
        string? conversationId)
    {
        return !string.IsNullOrWhiteSpace(prompt)
            && prompt.Trim().Length <= MaximumPromptLength
            && !string.IsNullOrWhiteSpace(conversationId);
    }

    public static TeamsReportRequestResult? Validate(
        string? prompt,
        string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new TeamsReportRequestResult(EmptyPromptMessage);
        }

        if (prompt.Trim().Length > MaximumPromptLength)
        {
            return new TeamsReportRequestResult(PromptTooLongMessage);
        }

        return string.IsNullOrWhiteSpace(conversationId)
            ? new TeamsReportRequestResult(InvalidConversationMessage)
            : null;
    }

    public async Task<TeamsReportRequestResult> HandleAsync(
        string? prompt,
        string? conversationId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var validationFailure = Validate(prompt, conversationId);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var trimmedPrompt = prompt!.Trim();
        var teamsConversationId = conversationId!.Trim();
        try
        {
            var plan = await _planningClient.PlanAsync(
                trimmedPrompt,
                teamsConversationId,
                cancellationToken);
            var request = new CopilotPlannedReportRequest
            {
                Prompt = trimmedPrompt,
                ConversationId = teamsConversationId,
                PreviousRequestId = null,
                Outcome = plan.Outcome,
                SemanticIntent = plan.SemanticIntent,
                UnresolvedConcepts = plan.UnresolvedConcepts,
                Clarification = plan.Clarification
            };
            var response = await _apiClient.CreatePlannedAsync(
                request,
                authorization,
                cancellationToken);

            try
            {
                await _apiClient.RegisterTargetAsync(
                    response.RequestId,
                    teamsConversationId,
                    cancellationToken);
                await _targetStore.SaveAsync(
                    new TeamsNotificationTarget(
                        response.RequestId,
                        teamsConversationId,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Could not register proactive notification target "
                        + "for request {RequestId}.",
                    response.RequestId);
            }

            if (!string.Equals(
                response.Status,
                "Received",
                StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Backend accepted report request with unexpected "
                        + "status {Status} for request {RequestId}.",
                    response.Status,
                    response.RequestId);
            }

            return new TeamsReportRequestResult(
                $"Rapor talebiniz alındı.{Environment.NewLine}"
                    + $"İşlem numarası: {response.RequestId}");
        }
        catch (BackendApiException exception)
        {
            _logger.LogWarning(
                exception,
                "Backend operation {OperationName} failed with HTTP "
                    + "status {HttpStatusCode}.",
                OperationName,
                (int)exception.StatusCode);

            return new TeamsReportRequestResult(
                exception.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        BackendAuthorizationFailureMessage,
                    HttpStatusCode.Forbidden =>
                        BackendForbiddenMessage,
                    _ => BackendFailureMessage
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CopilotStudioPlanningTimeoutException)
        {
            _logger.LogWarning(
                "Copilot Studio operation {OperationName} timed out.",
                OperationName);

            return new TeamsReportRequestResult(
                PlanningUnavailableMessage);
        }
        catch (CopilotStudioPlanningException)
        {
            _logger.LogWarning(
                "Copilot Studio operation {OperationName} failed safely.",
                OperationName);

            return new TeamsReportRequestResult(PlanningFailureMessage);
        }
        catch (TaskCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "Backend operation {OperationName} timed out.",
                OperationName);

            return new TeamsReportRequestResult(
                BackendUnavailableMessage);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Backend operation {OperationName} timed out.",
                OperationName);

            return new TeamsReportRequestResult(
                BackendUnavailableMessage);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected failure in operation {OperationName}.",
                OperationName);

            return new TeamsReportRequestResult(BackendFailureMessage);
        }
    }
}

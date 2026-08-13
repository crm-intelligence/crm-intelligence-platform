using System.Net;
using System.Text.Json;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Teams.Planning;
using Microsoft.Teams.Cards;
using AdaptiveCardVersion = Microsoft.Teams.Cards.Version;

namespace CrmAnalytics.Teams.Messaging;

public sealed class TeamsReportCardActionHandler
    : ITeamsReportCardActionHandler
{
    public const string ReviseVerb = "revise-report";
    public const string ClarificationVerb = "submit-clarification";
    public const int MaximumInputLength = 2000;
    public const string AuthorizationFailureTitle =
        "Oturum doğrulanamadı";
    public const string AuthorizationFailureMessage =
        "Oturumunuz doğrulanamadı veya bu işlem için yetkiniz "
        + "bulunmuyor. Lütfen yeniden oturum açmayı deneyin.";
    public const string PermissionFailureTitle =
        "İşlem için yetkiniz bulunmuyor";
    public const string RevisionPermissionFailureMessage =
        "Bu raporu revize etmek için yetkiniz veya veri erişim "
        + "kapsamınız bulunmuyor.";
    public const string ClarificationPermissionFailureMessage =
        "Bu rapora açıklama göndermek için yetkiniz veya veri erişim "
        + "kapsamınız bulunmuyor.";
    public const string SignInRequiredTitle =
        "Oturum açmanız gerekiyor";
    public const string SignInRequiredMessage =
        "İşleme devam etmek için kurumsal hesabınızla oturum açın. "
        + "Oturum açtıktan sonra kart üzerindeki işlemi yeniden "
        + "deneyin.";

    private readonly IReportRequestsApiClient _apiClient;
    private readonly ITeamsNotificationTargetStore _targetStore;
    private readonly ITeamsCardActionSubmissionStore _submissionStore;
    private readonly ITeamsClarificationActionQueue _clarificationQueue;
    private readonly ITeamsRevisionActionQueue _revisionQueue;
    private readonly ICopilotStudioPlanningClient _planningClient;
    private readonly ILogger<TeamsReportCardActionHandler> _logger;

    public TeamsReportCardActionHandler(
        IReportRequestsApiClient apiClient,
        ITeamsNotificationTargetStore targetStore,
        ITeamsCardActionSubmissionStore submissionStore,
        ITeamsClarificationActionQueue clarificationQueue,
        ITeamsRevisionActionQueue revisionQueue,
        ICopilotStudioPlanningClient planningClient,
        ILogger<TeamsReportCardActionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(targetStore);
        ArgumentNullException.ThrowIfNull(submissionStore);
        ArgumentNullException.ThrowIfNull(clarificationQueue);
        ArgumentNullException.ThrowIfNull(revisionQueue);
        ArgumentNullException.ThrowIfNull(planningClient);
        ArgumentNullException.ThrowIfNull(logger);

        _apiClient = apiClient;
        _targetStore = targetStore;
        _submissionStore = submissionStore;
        _clarificationQueue = clarificationQueue;
        _revisionQueue = revisionQueue;
        _planningClient = planningClient;
        _logger = logger;
    }

    public async Task<AdaptiveCard> HandleAsync(
        string? verb,
        IReadOnlyDictionary<string, object?> actionData,
        string? conversationId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actionData);
        ArgumentNullException.ThrowIfNull(authorization);

        if (verb is not ReviseVerb and not ClarificationVerb)
        {
            return CreateMessageCard(
                "İşlem desteklenmiyor",
                "Bu kart işlemi desteklenmiyor.",
                null);
        }

        if (string.IsNullOrWhiteSpace(conversationId)
            || !TryGetText(actionData, "requestId", out var requestId)
            || !TryGetText(actionData, "actionToken", out var actionToken))
        {
            return CreateSafeFailureCard();
        }

        var expectedConversationId = (await _apiClient.GetTargetAsync(
            requestId, cancellationToken))?.ConversationId;
        if (expectedConversationId is null)
            expectedConversationId = (await _targetStore.GetByRequestIdAsync(
                requestId, cancellationToken))?.ConversationId;
        if (!string.Equals(expectedConversationId,
            conversationId.Trim(), StringComparison.Ordinal))
        {
            return CreateSafeFailureCard();
        }

        var inputId = verb == ReviseVerb
            ? "revisionPrompt"
            : "clarificationResponse";
        if (!TryGetText(actionData, inputId, out var input)
            || input.Length is < 3 or > MaximumInputLength)
        {
            return CreateMessageCard(
                "Metni kontrol edin",
                "Lütfen 3 ile 2000 karakter arasında bir metin girin.",
                requestId);
        }

        var lockOwner = Guid.NewGuid().ToString("N");
        ClaimTeamsActionResponse? persistentClaim;
        try
        {
            persistentClaim = await _apiClient.ClaimActionAsync(actionToken,
                new ClaimTeamsActionRequest
                {
                    RequestId = requestId,
                    ActionType = verb,
                    LockOwner = lockOwner
                }, cancellationToken);
        }
        catch (BackendApiException)
        {
            return CreateSafeFailureCard();
        }

        if (string.Equals(persistentClaim?.Result, "Completed",
                StringComparison.Ordinal)
            || (persistentClaim is null
                && await _submissionStore.IsProcessedAsync(
                    actionToken, cancellationToken)))
        {
            return CreateMessageCard(
                "Talebiniz daha önce alındı",
                "Bu kart işlemi daha önce başarıyla işlendi.",
                requestId);
        }

        if (string.Equals(persistentClaim?.Result, "Busy",
            StringComparison.Ordinal))
        {
            return CreateMessageCard(
                "Talebiniz işleniyor",
                "Bu kart işlemi zaten işleniyor.",
                requestId);
        }

        if (verb == ClarificationVerb)
        {
            if (!_clarificationQueue.TryEnqueue(
                    new TeamsClarificationActionWorkItem(
                        requestId,
                        input,
                        actionToken,
                        lockOwner,
                        authorization)))
            {
                await TryReleaseAsync(actionToken, lockOwner);
                return CreateSafeClarificationFailureCard(requestId);
            }

            return CreateAcknowledgementCard(
                "Açıklamanız alındı",
                "Raporunuz verdiğiniz bilgiyle yeniden hazırlanıyor.",
                requestId);
        }

        if (!_revisionQueue.TryEnqueue(
                new TeamsRevisionActionWorkItem(
                    requestId,
                    input,
                    actionToken,
                    lockOwner,
                    conversationId.Trim(),
                    authorization)))
        {
            await TryReleaseAsync(actionToken, lockOwner);
            return CreateSafeRevisionFailureCard(requestId);
        }

        return CreateAcknowledgementCard(
            "Revizyon talebiniz alındı",
            "Raporunuz yeni talebe göre yeniden hazırlanıyor.",
            requestId);
    }

    private async Task<AdaptiveCard> ReviseAsync(
        string requestId,
        string revisionPrompt,
        string actionToken,
        string lockOwner,
        string conversationId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await _apiClient.GetPlanningContextAsync(
                requestId, authorization, cancellationToken);
            if (context is null
                || !string.Equals(
                    context.Status, "Completed", StringComparison.Ordinal))
            {
                await TryReleaseAsync(actionToken, lockOwner);
                return CreateSafeRevisionFailureCard(requestId);
            }

            var planned = await _planningClient.PlanAsync(
                new CopilotPhase2PlanningRequest(
                    "revision",
                    context.OriginalRequest,
                    context.CurrentSemanticPlan,
                    null,
                    revisionPrompt),
                context.ConversationId,
                cancellationToken);
            var response = await _apiClient.RevisePlannedAsync(
                requestId,
                new CopilotPlannedRevisionRequest
                {
                    RevisionInstruction = revisionPrompt,
                    Plan = ToSemanticPlan(planned)
                },
                authorization,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(response.RequestId)
                || string.Equals(
                    response.RequestId,
                    requestId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateMessageCard(
                    "Revizyon alınamadı",
                    "Revizyon talebiniz şu anda alınamadı. "
                        + "Lütfen tekrar deneyin.",
                    requestId);
            }

            await _apiClient.CompleteActionAsync(actionToken,
                new CompleteTeamsActionRequest
                {
                    LockOwner = lockOwner,
                    ResultRequestId = response.RequestId
                }, cancellationToken);
            await _apiClient.RegisterTargetAsync(
                response.RequestId, conversationId, cancellationToken);
            await _targetStore.SaveAsync(
                new TeamsNotificationTarget(
                    response.RequestId,
                    conversationId,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            await _submissionStore.MarkProcessedAsync(
                actionToken,
                cancellationToken);

            return CreateAcknowledgementCard(
                "Revizyon talebiniz alındı",
                "Raporunuz yeni talebe göre yeniden hazırlanıyor.",
                response.RequestId);
        }
        catch (BackendApiException exception)
        {
            await TryReleaseAsync(actionToken, lockOwner);
            _logger.LogWarning(
                "Backend card action failed with HTTP status {StatusCode}.",
                (int)exception.StatusCode);
            return exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    CreateAuthorizationFailureCard(),
                HttpStatusCode.Forbidden =>
                    CreatePermissionFailureCard(
                        RevisionPermissionFailureMessage),
                HttpStatusCode.Conflict => CreateMessageCard(
                    "Revizyon alınamadı",
                    "Bu rapor şu anda revize edilemiyor.",
                    requestId),
                HttpStatusCode.NotFound => CreateMessageCard(
                    "Rapor bulunamadı",
                    "Revize edilmek istenen rapor bulunamadı.",
                    requestId),
                _ => CreateMessageCard(
                    "Revizyon alınamadı",
                    "Revizyon talebiniz şu anda alınamadı. "
                        + "Lütfen tekrar deneyin.",
                    requestId)
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TryReleaseAsync(actionToken, lockOwner);
            _logger.LogError(
                "Unexpected failure while processing a revision "
                    + "card action.");
            return CreateMessageCard(
                "Revizyon alınamadı",
                "Revizyon talebiniz şu anda alınamadı. "
                    + "Lütfen tekrar deneyin.",
                requestId);
        }
    }

    private async Task<AdaptiveCard> SubmitClarificationAsync(
        string requestId,
        string clarificationResponse,
        string actionToken,
        string lockOwner,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await _apiClient.GetPlanningContextAsync(
                requestId, authorization, cancellationToken);
            if (context is null
                || !string.Equals(
                    context.Status,
                    "WaitingForClarification",
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(
                    context.ClarificationQuestion))
            {
                await TryReleaseAsync(actionToken, lockOwner);
                return CreateSafeClarificationFailureCard(requestId);
            }

            var planned = await _planningClient.PlanAsync(
                new CopilotPhase2PlanningRequest(
                    "clarification",
                    context.OriginalRequest,
                    context.CurrentSemanticPlan,
                    context.ClarificationQuestion,
                    clarificationResponse),
                context.ConversationId,
                cancellationToken);
            var response = await _apiClient.SubmitPlannedClarificationAsync(
                requestId,
                new CopilotPlannedClarificationRequest
                {
                    Answer = clarificationResponse,
                    Plan = ToSemanticPlan(planned)
                },
                authorization,
                cancellationToken);

            if (!string.Equals(
                response.RequestId,
                requestId,
                StringComparison.OrdinalIgnoreCase))
            {
                return CreateSafeClarificationFailureCard(requestId);
            }

            await _submissionStore.MarkProcessedAsync(
                actionToken,
                cancellationToken);
            await _apiClient.CompleteActionAsync(actionToken,
                new CompleteTeamsActionRequest
                {
                    LockOwner = lockOwner,
                    ResultRequestId = null
                }, cancellationToken);

            return CreateAcknowledgementCard(
                "Açıklamanız alındı",
                "Raporunuz verdiğiniz bilgiyle yeniden hazırlanıyor.",
                requestId);
        }
        catch (BackendApiException exception)
        {
            await TryReleaseAsync(actionToken, lockOwner);
            _logger.LogWarning(
                "Backend card action failed with HTTP status {StatusCode}.",
                (int)exception.StatusCode);
            return exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    CreateAuthorizationFailureCard(),
                HttpStatusCode.Forbidden =>
                    CreatePermissionFailureCard(
                        ClarificationPermissionFailureMessage),
                HttpStatusCode.Conflict => CreateMessageCard(
                    "Açıklama alınamadı",
                    "Bu rapor şu anda açıklama kabul etmiyor.",
                    requestId),
                HttpStatusCode.NotFound => CreateMessageCard(
                    "Rapor bulunamadı",
                    "Rapor talebi bulunamadı.",
                    requestId),
                _ => CreateSafeClarificationFailureCard(requestId)
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await TryReleaseAsync(actionToken, lockOwner);
            _logger.LogError(
                "Unexpected failure while processing a clarification "
                    + "card action.");
            return CreateSafeClarificationFailureCard(requestId);
        }
    }

    private static AdaptiveCard CreateAcknowledgementCard(
        string title,
        string description,
        string requestId) =>
        CreateMessageCard(
            title,
            description
                + " Sonuç hazır olduğunda yeni bir Teams bildirimi "
                + "alacaksınız.",
            requestId);

    private async Task TryReleaseAsync(string actionToken, string lockOwner)
    {
        try
        {
            await _apiClient.ReleaseActionAsync(actionToken,
                new ReleaseTeamsActionRequest { LockOwner = lockOwner },
                CancellationToken.None);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Persistent card action claim could not be released.");
        }
    }

    private static AdaptiveCard CreateSafeFailureCard() =>
        CreateMessageCard(
            "İşlem doğrulanamadı",
            "Bu kart işlemi güvenli biçimde doğrulanamadı.",
            null);

    private static AdaptiveCard CreateAuthorizationFailureCard() =>
        CreateMessageCard(
            AuthorizationFailureTitle,
            AuthorizationFailureMessage,
            null);

    private static AdaptiveCard CreatePermissionFailureCard(
        string message) =>
        CreateMessageCard(
            PermissionFailureTitle,
            message,
            null);

    public static AdaptiveCard CreateSignInRequiredCard() =>
        CreateMessageCard(
            SignInRequiredTitle,
            SignInRequiredMessage,
            null);

    private static AdaptiveCard CreateSafeClarificationFailureCard(
        string requestId) =>
        CreateMessageCard(
            "Açıklama alınamadı",
            "Açıklamanız şu anda alınamadı. Lütfen tekrar deneyin.",
            requestId);

    private static AdaptiveCard CreateSafeRevisionFailureCard(
        string requestId) =>
        CreateMessageCard(
            "Revizyon alÄ±namadÄ±",
            "Revizyon talebiniz ÅŸu anda alÄ±namadÄ±. LÃ¼tfen tekrar deneyin.",
            requestId);

    private static CopilotSemanticPlan ToSemanticPlan(
        CopilotPlannedReportRequest plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new CopilotSemanticPlan
        {
            Outcome = plan.Outcome,
            SemanticIntent = plan.SemanticIntent,
            UnresolvedConcepts = plan.UnresolvedConcepts,
            Clarification = plan.Clarification
        };
    }

    private static AdaptiveCard CreateMessageCard(
        string title,
        string message,
        string? requestId)
    {
        var body = new List<CardElement>
        {
            new TextBlock(title)
            {
                Size = new TextSize("Medium"),
                Weight = new TextWeight("Bolder"),
                Wrap = true
            },
            new TextBlock(message)
            {
                Wrap = true
            }
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            body.Add(new FactSet(
                new Fact("İşlem numarası", requestId)));
        }

        return new AdaptiveCard(body)
        {
            Schema = ReportNotificationCardFactory.AdaptiveCardSchema,
            Version = new AdaptiveCardVersion(
                ReportNotificationCardFactory.AdaptiveCardVersionValue),
            FallbackText = title
        };
    }

    private static bool TryGetText(
        IReadOnlyDictionary<string, object?> data,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        var text = raw switch
        {
            string stringValue => stringValue,
            JsonElement element
                when element.ValueKind == JsonValueKind.String =>
                    element.GetString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }
}

using System.Threading.Channels;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;

namespace CrmAnalytics.Teams.Messaging;

public sealed record TeamsClarificationActionWorkItem(
    string RequestId,
    string ClarificationResponse,
    string ActionToken,
    string LockOwner,
    BackendApiAuthorization Authorization);

public interface ITeamsClarificationActionQueue
{
    bool TryEnqueue(TeamsClarificationActionWorkItem workItem);

    IAsyncEnumerable<TeamsClarificationActionWorkItem> ReadAllAsync(
        CancellationToken cancellationToken);
}

internal sealed class TeamsClarificationActionQueue
    : ITeamsClarificationActionQueue
{
    private const int Capacity = 100;
    private readonly Channel<TeamsClarificationActionWorkItem> _channel =
        Channel.CreateBounded<TeamsClarificationActionWorkItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public bool TryEnqueue(TeamsClarificationActionWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.TryWrite(workItem);
    }

    public IAsyncEnumerable<TeamsClarificationActionWorkItem> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class TeamsClarificationActionWorker(
    ITeamsClarificationActionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TeamsClarificationActionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<
                    TeamsClarificationActionProcessor>();
                await processor.ProcessAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError(
                    "Unexpected failure in the clarification action worker.");
            }
        }
    }
}

internal sealed class TeamsClarificationActionProcessor(
    IReportRequestsApiClient apiClient,
    ITeamsCardActionSubmissionStore submissionStore,
    ICopilotStudioPlanningClient planningClient,
    ILogger<TeamsClarificationActionProcessor> logger)
{
    public async Task ProcessAsync(
        TeamsClarificationActionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        try
        {
            var context = await apiClient.GetPlanningContextAsync(
                workItem.RequestId,
                workItem.Authorization,
                cancellationToken);
            if (context is null
                || !string.Equals(
                    context.Status,
                    "WaitingForClarification",
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(
                    context.ClarificationQuestion))
            {
                await TryReleaseAsync(workItem);
                logger.LogWarning(
                    "Clarification action planning context was unavailable.");
                return;
            }

            var planned = await planningClient.PlanAsync(
                new CopilotPhase2PlanningRequest(
                    "clarification",
                    context.OriginalRequest,
                    context.CurrentSemanticPlan,
                    context.ClarificationQuestion,
                    workItem.ClarificationResponse),
                context.ConversationId,
                cancellationToken);
            var response = await apiClient.SubmitPlannedClarificationAsync(
                workItem.RequestId,
                new CopilotPlannedClarificationRequest
                {
                    Answer = workItem.ClarificationResponse,
                    Plan = ToSemanticPlan(planned)
                },
                workItem.Authorization,
                cancellationToken);

            if (!string.Equals(
                response.RequestId,
                workItem.RequestId,
                StringComparison.OrdinalIgnoreCase))
            {
                await TryReleaseAsync(workItem);
                logger.LogWarning(
                    "Clarification action returned a mismatched request id.");
                return;
            }

            await submissionStore.MarkProcessedAsync(
                workItem.ActionToken,
                cancellationToken);
            await apiClient.CompleteActionAsync(
                workItem.ActionToken,
                new CompleteTeamsActionRequest
                {
                    LockOwner = workItem.LockOwner,
                    ResultRequestId = null
                },
                cancellationToken);
        }
        catch (BackendApiException exception)
        {
            await TryReleaseAsync(workItem);
            logger.LogWarning(
                "Clarification card action failed with HTTP status {StatusCode}.",
                (int)exception.StatusCode);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseAsync(workItem);
            throw;
        }
        catch (Exception)
        {
            await TryReleaseAsync(workItem);
            logger.LogError(
                "Unexpected failure while processing a clarification "
                    + "card action.");
        }
    }

    private async Task TryReleaseAsync(
        TeamsClarificationActionWorkItem workItem)
    {
        try
        {
            await apiClient.ReleaseActionAsync(
                workItem.ActionToken,
                new ReleaseTeamsActionRequest
                {
                    LockOwner = workItem.LockOwner
                },
                CancellationToken.None);
        }
        catch (Exception)
        {
            logger.LogWarning(
                "Persistent clarification action claim could not be released.");
        }
    }

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
}

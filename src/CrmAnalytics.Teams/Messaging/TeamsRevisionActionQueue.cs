using System.Threading.Channels;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;

namespace CrmAnalytics.Teams.Messaging;

public sealed record TeamsRevisionActionWorkItem(
    string RequestId,
    string RevisionPrompt,
    string ActionToken,
    string LockOwner,
    string ConversationId,
    BackendApiAuthorization Authorization);

public interface ITeamsRevisionActionQueue
{
    bool TryEnqueue(TeamsRevisionActionWorkItem workItem);

    IAsyncEnumerable<TeamsRevisionActionWorkItem> ReadAllAsync(
        CancellationToken cancellationToken);
}

internal sealed class TeamsRevisionActionQueue
    : ITeamsRevisionActionQueue
{
    private const int Capacity = 100;

    private readonly Channel<TeamsRevisionActionWorkItem> _channel =
        Channel.CreateBounded<TeamsRevisionActionWorkItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public bool TryEnqueue(TeamsRevisionActionWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.TryWrite(workItem);
    }

    public IAsyncEnumerable<TeamsRevisionActionWorkItem> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class TeamsRevisionActionWorker(
    ITeamsRevisionActionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<TeamsRevisionActionWorker> logger)
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

                var processor = scope.ServiceProvider
                    .GetRequiredService<TeamsRevisionActionProcessor>();

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
                    "Unexpected failure in revision action worker.");
            }
        }
    }
}

internal sealed class TeamsRevisionActionProcessor(
    IReportRequestsApiClient apiClient,
    ITeamsNotificationTargetStore targetStore,
    ITeamsCardActionSubmissionStore submissionStore,
    ICopilotStudioPlanningClient planningClient,
    ILogger<TeamsRevisionActionProcessor> logger)
{
    public async Task ProcessAsync(
        TeamsRevisionActionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        try
        {
            var context = await apiClient.GetPlanningContextAsync(
                workItem.RequestId,
                workItem.Authorization,
                cancellationToken);

            if (context is null ||
                !string.Equals(
                    context.Status,
                    "Completed",
                    StringComparison.Ordinal))
            {
                await TryReleaseAsync(workItem);

                logger.LogWarning(
                    "Revision planning context was unavailable.");

                return;
            }

            var planned = await planningClient.PlanAsync(
                new CopilotPhase2PlanningRequest(
                    "revision",
                    context.OriginalRequest,
                    context.CurrentSemanticPlan,
                    null,
                    workItem.RevisionPrompt),
                context.ConversationId,
                cancellationToken);

            var response = await apiClient.RevisePlannedAsync(
                workItem.RequestId,
                new CopilotPlannedRevisionRequest
                {
                    RevisionInstruction = workItem.RevisionPrompt,
                    Plan = ToSemanticPlan(planned)
                },
                workItem.Authorization,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(response.RequestId) ||
                string.Equals(
                    response.RequestId,
                    workItem.RequestId,
                    StringComparison.OrdinalIgnoreCase))
            {
                await TryReleaseAsync(workItem);

                logger.LogWarning(
                    "Revision action returned an invalid child request id.");

                return;
            }

            await apiClient.RegisterTargetAsync(
                response.RequestId,
                workItem.ConversationId,
                cancellationToken);

            await targetStore.SaveAsync(
                new TeamsNotificationTarget(
                    response.RequestId,
                    workItem.ConversationId,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            await submissionStore.MarkProcessedAsync(
                workItem.ActionToken,
                cancellationToken);

            await apiClient.CompleteActionAsync(
                workItem.ActionToken,
                new CompleteTeamsActionRequest
                {
                    LockOwner = workItem.LockOwner,
                    ResultRequestId = response.RequestId
                },
                cancellationToken);
        }
        catch (BackendApiException exception)
        {
            await TryReleaseAsync(workItem);

            logger.LogWarning(
                "Revision card action failed with HTTP status {StatusCode}.",
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
                "Unexpected failure while processing revision action.");
        }
    }

    private async Task TryReleaseAsync(
        TeamsRevisionActionWorkItem workItem)
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
                "Persistent revision action claim could not be released.");
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

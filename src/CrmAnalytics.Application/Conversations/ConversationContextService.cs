using System.Collections.Concurrent;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Domain.Conversations;

namespace CrmAnalytics.Application.Conversations;

public sealed class ConversationContextService
    : IConversationContextService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        RegistrationGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly IConversationRepository _conversationRepository;
    private readonly IReportRequestRepository _reportRequestRepository;

    public ConversationContextService(
        IConversationRepository conversationRepository,
        IReportRequestRepository reportRequestRepository)
    {
        _conversationRepository = conversationRepository;
        _reportRequestRepository = reportRequestRepository;
    }

    public async Task RegisterRequestAsync(
        string teamsConversationId,
        string requestId,
        DateTimeOffset registeredAt,
        string? userId,
        CancellationToken cancellationToken)
    {
        await RegisterRequestCoreAsync(
            teamsConversationId,
            requestId,
            registeredAt,
            userId,
            tenantId: null,
            cancellationToken);
    }

    public async Task RegisterAuthenticatedRequestAsync(
        string teamsConversationId,
        string requestId,
        DateTimeOffset registeredAt,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await RegisterRequestCoreAsync(
            teamsConversationId,
            requestId,
            registeredAt,
            user.UserId,
            user.TenantId,
            cancellationToken);
    }

    private async Task RegisterRequestCoreAsync(
        string teamsConversationId,
        string requestId,
        DateTimeOffset registeredAt,
        string? userId,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var registrationKey = teamsConversationId.Trim();
        var registrationGate = RegistrationGates.GetOrAdd(
            registrationKey,
            static _ => new SemaphoreSlim(1, 1));

        await registrationGate.WaitAsync(cancellationToken);

        try
        {
            var conversation =
                await _conversationRepository.GetByTeamsConversationIdAsync(
                    teamsConversationId,
                    cancellationToken);

            if (conversation is not null)
            {
                EnsureConversationOwner(conversation, userId, tenantId);
                conversation.UpdateLastRequest(requestId, registeredAt);
                await _conversationRepository.UpdateAsync(
                    conversation,
                    cancellationToken);
                return;
            }

            var newConversation = Conversation.Create(
                id: Guid.NewGuid().ToString("N"),
                teamsConversationId: teamsConversationId,
                lastRequestId: requestId,
                createdAt: registeredAt,
                userId: userId,
                tenantId: tenantId);

            try
            {
                await _conversationRepository.AddAsync(
                    newConversation,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                conversation =
                    await _conversationRepository.GetByTeamsConversationIdAsync(
                        teamsConversationId,
                        cancellationToken);

                if (conversation is null)
                {
                    throw;
                }

                EnsureConversationOwner(conversation, userId, tenantId);
                conversation.UpdateLastRequest(requestId, registeredAt);
                await _conversationRepository.UpdateAsync(
                    conversation,
                    cancellationToken);
            }
        }
        finally
        {
            registrationGate.Release();
        }
    }

    public async Task<string?> GetLastRequestIdAsync(
        string teamsConversationId,
        CancellationToken cancellationToken)
    {
        var conversation =
            await _conversationRepository.GetByTeamsConversationIdAsync(
                teamsConversationId,
                cancellationToken);

        return conversation?.LastRequestId;
    }

    public async Task<IReadOnlyList<ConversationReportRequestHistoryItem>>
        GetHistoryAsync(
            string teamsConversationId,
            CancellationToken cancellationToken)
    {
        return await GetHistoryCoreAsync(
            teamsConversationId,
            user: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationReportRequestHistoryItem>>
        GetHistoryAsync(
            string teamsConversationId,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await GetHistoryCoreAsync(
            teamsConversationId,
            user,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ConversationReportRequestHistoryItem>>
        GetHistoryCoreAsync(
            string teamsConversationId,
            AuthenticatedUserContext? user,
            CancellationToken cancellationToken)
    {
        var reportRequests =
            await _reportRequestRepository.GetByConversationIdAsync(
                teamsConversationId,
                cancellationToken);

        return reportRequests
            .Where(reportRequest =>
                user is null
                || (IdentifiersEqual(reportRequest.UserId, user.UserId)
                    && IdentifiersEqual(
                        reportRequest.TenantId,
                        user.TenantId)))
            .Select(reportRequest =>
                new ConversationReportRequestHistoryItem(
                    RequestId: reportRequest.RequestId,
                    PreviousRequestId: reportRequest.PreviousRequestId,
                    Status: reportRequest.Status,
                    CreatedAt: reportRequest.CreatedAt,
                    UpdatedAt: reportRequest.UpdatedAt,
                    Summary: reportRequest.Summary,
                    PowerBiUrl: reportRequest.PowerBiUrl,
                    ClarificationQuestion:
                        reportRequest.ClarificationQuestion,
                    RejectionMessage:
                        reportRequest.RejectionMessage))
            .ToArray();
    }

    private static void EnsureConversationOwner(
        Conversation conversation,
        string? userId,
        string? tenantId)
    {
        var userMatches = userId is null
            ? conversation.UserId is null
            : IdentifiersEqual(conversation.UserId, userId);
        var tenantMatches = tenantId is null
            ? conversation.TenantId is null
            : IdentifiersEqual(conversation.TenantId, tenantId);

        if (!userMatches || !tenantMatches)
        {
            throw new InvalidOperationException(
                "The conversation belongs to a different identity.");
        }
    }

    private static bool IdentifiersEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        return Guid.TryParse(left.Trim(), out var leftGuid)
            && Guid.TryParse(right.Trim(), out var rightGuid)
            ? leftGuid == rightGuid
            : string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.Ordinal);
    }
}

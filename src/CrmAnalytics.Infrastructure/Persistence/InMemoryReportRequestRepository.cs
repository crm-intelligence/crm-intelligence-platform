using System.Collections.Concurrent;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Infrastructure.Persistence;

public sealed class InMemoryReportRequestRepository
    : IReportRequestRepository
{
    private readonly ConcurrentDictionary<string, ReportRequest> _requests =
        new(StringComparer.OrdinalIgnoreCase);

    public Task AddAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);

        cancellationToken.ThrowIfCancellationRequested();

        var added = _requests.TryAdd(
            reportRequest.RequestId,
            reportRequest);

        if (!added)
        {
            throw new InvalidOperationException(
                $"A report request with ID '{reportRequest.RequestId}' already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<ReportRequest?> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        cancellationToken.ThrowIfCancellationRequested();

        _requests.TryGetValue(requestId, out var reportRequest);

        return Task.FromResult<ReportRequest?>(reportRequest);
    }

    public Task<IReadOnlyList<ReportRequest>> GetByConversationIdAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ReportRequest> reportRequests = _requests.Values
            .Where(reportRequest => string.Equals(
                reportRequest.ConversationId,
                conversationId.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(reportRequest => reportRequest.CreatedAt)
            .ThenBy(reportRequest => reportRequest.RequestId)
            .ToArray();

        return Task.FromResult(reportRequests);
    }

    public Task UpdateAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);

        cancellationToken.ThrowIfCancellationRequested();

        while (_requests.TryGetValue(
            reportRequest.RequestId,
            out var currentReportRequest))
        {
            if (_requests.TryUpdate(
                reportRequest.RequestId,
                reportRequest,
                currentReportRequest))
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new KeyNotFoundException(
            $"A report request with ID '{reportRequest.RequestId}' was not found.");
    }
}

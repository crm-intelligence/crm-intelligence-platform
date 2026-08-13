using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.Abstractions.Persistence;

public interface IReportRequestRepository
{
    Task AddAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken);

    Task<ReportRequest?> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportRequest>> GetByConversationIdAsync(
        string conversationId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken);
}

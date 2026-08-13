using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class SqlReportRequestRepository
    : IReportRequestRepository
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlReportRequestRepository(CrmAnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);

        await _dbContext.ReportRequests.AddAsync(
            reportRequest,
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerExceptionClassifier.IsDuplicateKey(exception))
        {
            _dbContext.Entry(reportRequest).State = EntityState.Detached;
            throw new InvalidOperationException(
                "A report request with the same identity already exists.");
        }
    }

    public async Task<ReportRequest?> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        return await _dbContext.ReportRequests.SingleOrDefaultAsync(
            reportRequest =>
                reportRequest.RequestId == requestId.Trim(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ReportRequest>>
        GetByConversationIdAsync(
            string conversationId,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var normalizedConversationId = conversationId.Trim();

        return await _dbContext.ReportRequests
            .AsNoTracking()
            .Where(reportRequest =>
                reportRequest.ConversationId ==
                    normalizedConversationId)
            .OrderBy(reportRequest => reportRequest.CreatedAt)
            .ThenBy(reportRequest => reportRequest.RequestId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        ReportRequest reportRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);

        var entry = _dbContext.Entry(reportRequest);

        if (entry.State == EntityState.Detached)
        {
            var trackedReportRequest =
                await _dbContext.ReportRequests.SingleOrDefaultAsync(
                    candidate =>
                        candidate.RequestId == reportRequest.RequestId,
                    cancellationToken);

            if (trackedReportRequest is null)
            {
                throw new KeyNotFoundException(
                    "The report request was not found.");
            }

            _dbContext.Entry(trackedReportRequest)
                .CurrentValues.SetValues(reportRequest);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(exception);
        }
    }
}

using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class MockQueryPlanningClient : IQueryPlanningClient
{
    private const string ClarificationQuestion =
        "Analiz için tarih aralığını belirtir misiniz?";

    private readonly MockExternalServicesOptions _options;

    public MockQueryPlanningClient(
        IOptions<MockExternalServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<QueryPlanningResponse> PlanAsync(
        QueryPlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Delay(
            _options.DelayMilliseconds,
            cancellationToken);

        if (_options.ForceQueryPlanningFailure)
        {
            return new QueryPlanningResponse(
                Status: ExternalOperationStatus.Failed,
                CanonicalRequest: null,
                ClarificationQuestion: null,
                GeneratedQueryReference: null,
                Error: new ExternalServiceError(
                    Code: "QUERY_PLANNING_FAILED",
                    Message: "Query planning could not be completed.",
                    IsTransient: false));
        }

        if (_options.ForceClarification)
        {
            return new QueryPlanningResponse(
                Status: ExternalOperationStatus.WaitingForClarification,
                CanonicalRequest: null,
                ClarificationQuestion: ClarificationQuestion,
                GeneratedQueryReference: null,
                Error: null);
        }

        var canonicalRequest = JsonSerializer.Serialize(new
        {
            requestId = request.RequestId,
            previousRequestId = request.PreviousRequestId,
            isRevision = request.PreviousRequestId is not null,
            dataScope = new
            {
                allowAllRegions =
                    request.UserDataScope.AllowAllRegions,
                allowAllStores =
                    request.UserDataScope.AllowAllStores,
                regionCount =
                    request.UserDataScope.AllowedRegions.Count,
                storeCount =
                    request.UserDataScope.AllowedStoreIds.Count
            }
        });

        return new QueryPlanningResponse(
            Status: ExternalOperationStatus.Completed,
            CanonicalRequest: canonicalRequest,
            ClarificationQuestion: null,
            GeneratedQueryReference:
                $"query-result://{request.RequestId}",
            Error: null);
    }
}

using System.Net;
using System.Net.Http.Json;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Contracts.SqlAgent;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class SqlAgentEndpointTests
{
    private const string OwnerUser = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string OwnerTenant = "22222222-2222-4222-8222-222222222222";
    private const string OtherUser = "11111111-1111-4111-8111-111111111111";
    private const string OtherTenant = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    private const string ValidCandidate =
        """
        SELECT
            customer_state,
            SUM(CASE
                WHEN order_purchase_timestamp >= '2018-07-01' THEN price
                ELSE 0
            END) AS current_value,
            SUM(CASE
                WHEN order_purchase_timestamp < '2018-07-01' THEN price
                ELSE 0
            END) AS previous_value
        FROM mart.vw_sales
        WHERE order_purchase_timestamp >= '2018-01-01'
          AND order_purchase_timestamp <= '2018-12-31'
        GROUP BY customer_state
        """;

    [Fact]
    public async Task SemanticTool_RequiresAuthentication()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/tools/query-context",
            IntentRequest("missing", agentic: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task QueryContext_ReturnsOnlyIntentScopedApprovedContext()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestId = "sql-agent-context";
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/tools/query-context",
            IntentRequest(requestId, agentic: true));
        var context = await response.Content
            .ReadFromJsonAsync<SqlAgentQueryContextResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(context);
        Assert.Equal("succeeded", context.Status);
        Assert.Equal(64, context.ContextFingerprint.Length);
        Assert.Equal("item_sales", Assert.Single(context.Metrics).Key);
        Assert.DoesNotContain(context.Metrics, metric => metric.Key == "payment_total");
        Assert.Contains(context.Dimensions, dimension => dimension.Key == "customer_state");
        Assert.DoesNotContain(context.Dimensions, dimension => dimension.Key == "payment_type");
        var source = Assert.Single(context.Sources);
        Assert.Equal("mart.vw_sales", source.ApprovedPhysicalObject);
        Assert.DoesNotContain("payment_value", source.ApprovedColumns);
        Assert.Equal(0, context.Capabilities.MaximumExecutableJoins);
        Assert.False(context.Capabilities.JoinExecutionEnabled);
        Assert.Empty(context.Relationships);
    }

    [Fact]
    public async Task MetricTool_UnrelatedKey_ReturnsControlledResponseWithoutCatalogData()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestId = "sql-agent-unrelated";
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);
        var request = new SqlAgentMetricContextRequest
        {
            RequestId = requestId,
            Intent = Intent(agentic: true),
            MetricKey = "payment_total"
        };

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/tools/metric-context", request);
        var error = await response.Content.ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported", error?.Status);
        Assert.Equal("UNRELATED_SEMANTIC_CONTEXT", error?.ReasonCode);
        Assert.DoesNotContain("payment_value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CapabilityDecision_IsBackendAuthoritativeForV1AdvancedAndUnsupported()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestId = "sql-agent-capability";
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);

        var deterministic = await AnalyzeAsync(client, requestId, Intent(agentic: false));
        var agentic = await AnalyzeAsync(client, requestId, Intent(agentic: true));
        var unsupportedIntent = Intent(agentic: false) with
        {
            Metrics = ["unknown_metric"]
        };
        var unsupported = await AnalyzeAsync(client, requestId, unsupportedIntent);

        Assert.Equal("deterministic", deterministic.Outcome);
        Assert.Equal("agentic_required", agentic.Outcome);
        Assert.Contains("period_comparison", agentic.Features);
        Assert.Equal("unsupported", unsupported.Outcome);
        Assert.Contains("unknown_metric", unsupported.Reasons);
        Assert.Null(unsupported.ContextFingerprint);
    }

    [Fact]
    public async Task ValidCandidate_UsesCommonGuardrailsAndProducesInternalExecutionPlanMetadata()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestId = "sql-agent-valid-candidate";
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);
        var context = await GetContextAsync(client, requestId);

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/candidates",
            Candidate(requestId, context.ContextFingerprint, ValidCandidate));
        var accepted = await response.Content
            .ReadFromJsonAsync<SqlAgentCandidateSubmissionResponse>();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("succeeded", accepted?.Status);
        Assert.Equal("agentic", accepted?.Strategy);
        Assert.Equal("dwh", accepted?.Source);
        Assert.Equal("mart.vw_sales", accepted?.VerifiedPhysicalObject);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@scope", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2018-07-01", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DELETE FROM mart.vw_sales;", "GR001")]
    [InlineData("SELECT * FROM mart.vw_sales;", "GR004")]
    [InlineData("SELECT confidential_value FROM dbo.secret_table;", "GR003")]
    [InlineData("SELECT secret_column FROM mart.vw_sales;", "GR004")]
    [InlineData("SELECT customer_state, SUM(price) FROM mart.vw_sales WHERE customer_state = @scope0 GROUP BY customer_state;", "GR014")]
    public async Task MaliciousCandidate_IsRejectedByExistingSecurityPipeline(
        string candidateSql,
        string reasonCode)
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        var requestId = "sql-agent-malicious-" + Guid.NewGuid().ToString("N");
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);
        var context = await GetContextAsync(client, requestId);

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/candidates",
            Candidate(requestId, context.ContextFingerprint, candidateSql));
        var error = await response.Content.ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("rejected", error?.Status);
        Assert.Equal(reasonCode, error?.ReasonCode);
    }

    [Fact]
    public async Task CandidateFingerprint_CannotBeAttachedToAnotherRequest()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestA = "sql-agent-binding-a";
        const string requestB = "sql-agent-binding-b";
        await SeedProcessingRequestAsync(factory.Services, requestA, OwnerUser, OwnerTenant);
        await SeedProcessingRequestAsync(factory.Services, requestB, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);
        var contextA = await GetContextAsync(client, requestA);

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/candidates",
            Candidate(requestB, contextA.ContextFingerprint, ValidCandidate));
        var error = await response.Content.ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CANDIDATE_REQUEST_BINDING_MISMATCH", error?.ReasonCode);
    }

    [Theory]
    [InlineData("payment_total", "customer_state")]
    [InlineData("customer_state", null)]
    public async Task CandidateSemanticDeclarations_MustMatchCurrentIntent(
        string firstKey,
        string? secondKey)
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        var requestId = "sql-agent-semantic-" + Guid.NewGuid().ToString("N");
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var client = OwnerClient(factory);
        var context = await GetContextAsync(client, requestId);
        var candidate = Candidate(requestId, context.ContextFingerprint, ValidCandidate) with
        {
            ReferencedSemanticKeys = secondKey is null
                ? [firstKey]
                : [firstKey, secondKey]
        };

        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/candidates", candidate);
        var error = await response.Content.ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("GR014", error?.ReasonCode);
    }

    [Fact]
    public async Task CandidateSubmission_HidesRequestsOwnedByAnotherUserAndTenant()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string requestId = "sql-agent-owner";
        await SeedProcessingRequestAsync(factory.Services, requestId, OwnerUser, OwnerTenant);
        using var owner = OwnerClient(factory);
        var context = await GetContextAsync(owner, requestId);
        using var other = AuthenticatedClient(factory, OtherUser, OtherTenant);

        var response = await other.PostAsJsonAsync(
            "/api/sql-agent/candidates",
            Candidate(requestId, context.ContextFingerprint, ValidCandidate));
        var error = await response.Content.ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("REQUEST_NOT_FOUND", error?.ReasonCode);
    }

    [Fact]
    public async Task OpenApi_ContainsStableSqlAgentOperationNames()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json");

        Assert.Contains("get_query_semantic_context", document, StringComparison.Ordinal);
        Assert.Contains("analyze_query_capability", document, StringComparison.Ordinal);
        Assert.Contains("submit_sql_candidate", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/sql-agent/catalog", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/sql-agent/schema", document, StringComparison.Ordinal);
    }

    private static async Task<SqlAgentCapabilityResponse> AnalyzeAsync(
        HttpClient client,
        string requestId,
        CopilotSqlAgentIntent intent)
    {
        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/capabilities/analyze",
            new SqlAgentIntentRequest { RequestId = requestId, Intent = intent });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SqlAgentCapabilityResponse>())!;
    }

    private static async Task<SqlAgentQueryContextResponse> GetContextAsync(
        HttpClient client,
        string requestId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/sql-agent/tools/query-context",
            IntentRequest(requestId, agentic: true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SqlAgentQueryContextResponse>())!;
    }

    private static SqlAgentIntentRequest IntentRequest(string requestId, bool agentic) =>
        new() { RequestId = requestId, Intent = Intent(agentic) };

    private static CopilotSqlAgentIntent Intent(bool agentic) => new()
    {
        Metrics = ["item_sales"],
        Dimensions = ["customer_state"],
        Filters = [],
        Time = new SqlAgentTimeIntent
        {
            Range = new SqlAgentDateRange
            {
                Kind = SqlAgentDateRangeKind.Absolute,
                From = new DateOnly(2018, 1, 1),
                To = new DateOnly(2018, 12, 31)
            }
        },
        Comparisons = agentic
            ? [new SqlAgentPeriodComparison
            {
                Kind = SqlAgentPeriodComparisonKind.PreviousPeriod
            }]
            : []
    };

    private static SqlAgentCandidateSubmissionRequest Candidate(
        string requestId,
        string fingerprint,
        string sql) => new()
        {
            RequestId = requestId,
            Intent = Intent(agentic: true),
            ContextFingerprint = fingerprint,
            CandidateSql = sql,
            ReferencedSemanticKeys = ["item_sales", "customer_state"],
            ReferencedRelationshipIds = []
        };

    private static async Task SeedProcessingRequestAsync(
        IServiceProvider services,
        string requestId,
        string userId,
        string tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var report = ReportRequest.Create(
            requestId,
            "conversation-" + requestId,
            null,
            "Create an analytical report.",
            "correlation-" + requestId,
            userId,
            tenantId,
            now);
        report.TransitionTo(ReportRequestStatus.Validating, now.AddSeconds(1));
        report.TransitionTo(ReportRequestStatus.Processing, now.AddSeconds(2));
        report.RecordCanonicalRequest(
            CanonicalRequestSerializer.Serialize(Canonical(report)),
            now.AddSeconds(3));

        using var scope = services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReportRequestRepository>();
        await repository.AddAsync(report, CancellationToken.None);
    }

    private static CanonicalRequest Canonical(ReportRequest report) => new()
    {
        RequestId = report.RequestId,
        ConversationId = report.ConversationId,
        Source = DataSource.Dwh,
        Intent = RequestIntent.Breakdown,
        Metrics = ["item_sales"],
        Dimensions = ["customer_state"],
        Filters = [],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 12, 31)
        },
        Confidence = 1
    };

    private static HttpClient OwnerClient(SecuredTestWebApplicationFactory factory) =>
        AuthenticatedClient(factory, OwnerUser, OwnerTenant);

    private static HttpClient AuthenticatedClient(
        SecuredTestWebApplicationFactory factory,
        string userId,
        string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.UserIdHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.TenantIdHeader, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.ScopesHeader, "access_as_user");
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.RolesHeader, "Report.User");
        return client;
    }
}

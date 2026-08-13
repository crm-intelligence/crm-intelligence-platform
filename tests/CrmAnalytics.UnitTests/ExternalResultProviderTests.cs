using System.Net;
using System.Text;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ExternalResultProviderTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid ReportId = Guid.NewGuid();

    [Fact]
    public void ProtectedEnvironment_RejectsMockProviders()
    {
        var environment = new TestEnvironment(Environments.Production);
        var analytics = new AnalyticsOptionsValidator(environment).Validate(
            null, new AnalyticsOptions { Provider = AnalyticsProviders.Mock });
        var reporting = new ReportingOptionsValidator(environment).Validate(
            null, new ReportingOptions { Provider = ReportingProviders.Mock });

        Assert.True(analytics.Failed);
        Assert.True(reporting.Failed);
    }

    [Fact]
    public void FabricProvider_ValidIdsStillFailsClosedWithoutStagingContract()
    {
        var options = CreateAnalyticsOptions();
        var result = new AnalyticsOptionsValidator(
            new TestEnvironment(Environments.Production)).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains(
            "Blob/OneLake/Delta", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerBiOptions_RequireSemanticModelOnlyForRefresh()
    {
        var options = CreateReportingOptions();
        var validator = new ReportingOptionsValidator(
            new TestEnvironment(Environments.Production));
        Assert.True(validator.Validate(null, options).Succeeded);

        options.PowerBi.RefreshBeforeReturn = true;
        Assert.True(validator.Validate(null, options).Failed);
        options.PowerBi.SemanticModelId = Guid.NewGuid().ToString();
        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("http://app.powerbi.com/groups/1")]
    [InlineData("https://evil.example/groups/1")]
    [InlineData("https://user@app.powerbi.com/groups/1")]
    [InlineData("not-a-url")]
    public void PowerBiWebUrl_RejectsUnsafeValues(string value) =>
        Assert.False(PowerBiReportApiClient.TryValidateWebUrl(value, out _));

    [Fact]
    public async Task DirectAnalytics_UsesOnlyBoundedMetadata()
    {
        var client = new DirectAnalyticsClient();
        var query = new QueryExecutionResult(
            "qry_1", SqlDataSource.Dwh,
            [new QueryResultColumn(0, "CustomerName",
                QueryResultValueKind.String, false)],
            [new QueryResultRow([QueryResultValue.Create(
                QueryResultValueKind.String, "Sensitive Person")])],
            true, 10, DateTimeOffset.UtcNow, TimeSpan.Zero);

        var response = await client.AnalyzeAsync(
            CreateAnalyticsRequest(query), CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, response.Status);
        Assert.Contains("1 rows", response.Summary);
        Assert.DoesNotContain("Sensitive Person", response.Summary);
        Assert.DoesNotContain("CustomerName", response.Summary);
    }

    [Fact]
    public async Task FabricClient_RunsAndPollsWithBearerToken()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.Accepted, location:
                $"https://api.fabric.microsoft.com/v1/operations/{ItemId:D}"),
            _ => Response(HttpStatusCode.OK, "{\"status\":\"Completed\"}"));
        var client = new FabricJobClient(
            new SingleClientFactory(handler), new FabricTokenProvider(),
            Options.Create(CreateAnalyticsOptions()));

        var result = await client.RunToCompletionAsync(CancellationToken.None);

        Assert.Equal(ItemId.ToString("D"), result.JobInstanceId);
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer", request.AuthorizationScheme));
        Assert.All(handler.Requests, request =>
            Assert.Equal("fake-token", request.AuthorizationParameter));
        Assert.DoesNotContain(handler.Bodies, body => !string.IsNullOrEmpty(body));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FabricClient_DoesNotRetryOrExposePermanentAuthBody(
        HttpStatusCode statusCode)
    {
        var handler = new SequenceHandler(
            _ => Response(statusCode, "secret technical body"));
        var client = new FabricJobClient(
            new SingleClientFactory(handler), new FabricTokenProvider(),
            Options.Create(CreateAnalyticsOptions()));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RunToCompletionAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.DoesNotContain("secret technical body", exception.Message);
    }

    [Fact]
    public async Task FabricClient_ConvertsBoundedCancellationToTimeout()
    {
        var options = CreateAnalyticsOptions();
        options.Fabric.TimeoutSeconds = 1;
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.Accepted, location:
                $"https://api.fabric.microsoft.com/v1/operations/{ItemId:D}"),
            _ => Response(HttpStatusCode.OK, "{\"status\":\"Running\"}"));
        var client = new FabricJobClient(
            new SingleClientFactory(handler), new FabricTokenProvider(),
            Options.Create(options));

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.RunToCompletionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PowerBiApi_Retries429AndReturnsOnlySafeWebUrl()
    {
        var first = Response(HttpStatusCode.TooManyRequests);
        first.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.Zero);
        var handler = new SequenceHandler(
            _ => first,
            _ => Response(HttpStatusCode.OK,
                $"{{\"id\":\"{ReportId:D}\",\"webUrl\":\"https://app.powerbi.com/groups/me/reports/{ReportId:D}\",\"embedUrl\":\"https://app.powerbi.com/embed?token=secret\"}}"));
        var api = new PowerBiReportApiClient(
            new SingleClientFactory(handler), new PowerBiTokenProvider(),
            Options.Create(CreateReportingOptions()));

        var report = await api.GetConfiguredReportAsync(CancellationToken.None);

        Assert.Equal(ReportId.ToString("D"), report.ReportId);
        Assert.DoesNotContain("token", report.WebUrl.AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PowerBiRefresh_DisabledMakesNoApiRequest()
    {
        var options = CreateReportingOptions();
        var handler = new SequenceHandler();
        var api = new PowerBiReportApiClient(
            new SingleClientFactory(handler), new PowerBiTokenProvider(),
            Options.Create(options));

        await api.RefreshSemanticModelAsync(CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PowerBiRefresh_AcceptedWithoutPollingReturnsPromptly()
    {
        var options = CreateReportingOptions();
        options.PowerBi.RefreshBeforeReturn = true;
        options.PowerBi.SemanticModelId = Guid.NewGuid().ToString();
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.Accepted));
        var api = new PowerBiReportApiClient(
            new SingleClientFactory(handler), new PowerBiTokenProvider(),
            Options.Create(options));

        await api.RefreshSemanticModelAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PowerBiReportClient_MapsFailureToSafeResult()
    {
        var client = new PowerBiReportClient(
            new FailingPowerBiApiClient(),
            Options.Create(CreateReportingOptions()));

        var response = await client.GenerateAsync(
            new ReportGenerationRequest("req", "PowerBi", "ref"),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, response.Status);
        Assert.Null(response.PowerBiUrl);
        Assert.DoesNotContain("technical-body", response.Error!.Message);
    }

    private static AnalyticsExecutionRequest CreateAnalyticsRequest(
        QueryExecutionResult? result) => new(
            new AnalyticsRequest("request-1", "Sales", "qry_1",
                new Dictionary<string, string?>()), result);

    private static AnalyticsOptions CreateAnalyticsOptions() => new()
    {
        Provider = AnalyticsProviders.FabricJob,
        Fabric = new FabricOptions
        {
            WorkspaceId = WorkspaceId.ToString(),
            ItemId = ItemId.ToString(),
            JobType = "Pipeline",
            PollingIntervalSeconds = 1,
            TimeoutSeconds = 5,
            MaxRetries = 2,
            AuthenticationMode = "ManagedIdentity"
        }
    };

    private static ReportingOptions CreateReportingOptions() => new()
    {
        Provider = ReportingProviders.PowerBi,
        PowerBi = new PowerBiOptions
        {
            WorkspaceId = WorkspaceId.ToString(),
            ReportId = ReportId.ToString(),
            RefreshBeforeReturn = false,
            AuthenticationMode = "ManagedIdentity"
        }
    };

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body = "{}",
        string? location = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (location is not null)
            response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class FabricTokenProvider : IFabricAccessTokenProvider
    {
        public ValueTask<string> GetAccessTokenAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult("fake-token");
    }

    private sealed class PowerBiTokenProvider : IPowerBiAccessTokenProvider
    {
        public ValueTask<string> GetAccessTokenAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult("fake-token");
    }

    private sealed class FailingPowerBiApiClient : IPowerBiReportApiClient
    {
        public Task<PowerBiReportMetadata> GetConfiguredReportAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("technical-body");

        public Task RefreshSemanticModelAsync(
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler)
        : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class SequenceHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int _index;
        public List<RequestSnapshot> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responses[_index++](request);
        }
    }

    private sealed record RequestSnapshot(
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}

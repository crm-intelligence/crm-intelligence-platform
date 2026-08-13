using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Planning;

public sealed class DirectLineCopilotStudioPlanningClient
    : ICopilotStudioPlanningClient
{
    public const string HttpClientName =
        "CrmAnalytics.Teams.CopilotStudioDirectLine";

    private const string DirectLineApiPath = "v3/directline";
    private const int MaximumProtocolResponseLength = 1_048_576;
    private const int MaximumSemanticPayloadLength = 65_536;
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions StrictJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly HttpClient _httpClient;
    private readonly CopilotStudioOptions _options;
    private readonly ILogger<DirectLineCopilotStudioPlanningClient> _logger;
    private readonly Uri _directLineBaseUri;

    public DirectLineCopilotStudioPlanningClient(
        HttpClient httpClient,
        IOptions<CopilotStudioOptions> options,
        ILogger<DirectLineCopilotStudioPlanningClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _directLineBaseUri = new Uri(
            _options.DirectLineBaseUri.TrimEnd('/') + "/",
            UriKind.Absolute);
    }

    public async Task<CopilotPlannedReportRequest> PlanAsync(
        string prompt,
        string conversationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        if (!_options.Enabled)
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio semantic planning is disabled.");
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);

        try
        {
            var directLineUserId = CreateDirectLineUserId();
            var token = await GenerateTokenAsync(
                directLineUserId,
                linkedCancellation.Token);
            var conversation = await StartConversationAsync(
                token.Token,
                linkedCancellation.Token);
            var directLineToken = string.IsNullOrWhiteSpace(
                conversation.Token)
                ? token.Token
                : conversation.Token;

            await SendPromptAsync(
                conversation.ConversationId,
                directLineToken,
                directLineUserId,
                prompt.Trim(),
                linkedCancellation.Token);
            var semanticPlan = await WaitForPlanAsync(
                conversation.ConversationId,
                directLineToken,
                directLineUserId,
                linkedCancellation.Token);

            var result = new CopilotPlannedReportRequest
            {
                Prompt = prompt.Trim(),
                ConversationId = conversationId.Trim(),
                PreviousRequestId = null,
                Outcome = semanticPlan.Outcome,
                SemanticIntent = semanticPlan.SemanticIntent,
                UnresolvedConcepts = semanticPlan.UnresolvedConcepts,
                Clarification = semanticPlan.Clarification
            };
            ValidatePlan(result);
            return result;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested
                && timeout.IsCancellationRequested)
        {
            throw new CopilotStudioPlanningTimeoutException(
                "Copilot Studio semantic planning timed out.",
                exception);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CopilotStudioPlanningException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio semantic planning failed.",
                exception);
        }
    }

    private async Task<DirectLineToken> GenerateTokenAsync(
        string directLineUserId,
        CancellationToken cancellationToken)
    {
        using var request = CreateDirectLineRequest(
            HttpMethod.Post,
            CreateDirectLineUri("tokens/generate"),
            _options.DirectLineSecret);
        request.Content = JsonContent.Create(new
        {
            user = new { id = directLineUserId }
        });
        using var response = await SendDirectLineAsync(
            request,
            "token-generation",
            cancellationToken,
            directLineUserId);
        EnsureSuccess(response, HttpStatusCode.OK, "token generation");
        return await ReadTokenAsync(
            response,
            cancellationToken);
    }

    private async Task<DirectLineConversation> StartConversationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateDirectLineRequest(
            HttpMethod.Post,
            CreateDirectLineUri("conversations"),
            token);
        using var response = await SendDirectLineAsync(
            request,
            "conversation-start",
            cancellationToken);
        EnsureSuccess(
            response,
            [HttpStatusCode.OK, HttpStatusCode.Created],
            "conversation start");
        return await ReadConversationAsync(
            response,
            requireConversationId: true,
            cancellationToken);
    }

    private async Task SendPromptAsync(
        string directLineConversationId,
        string token,
        string directLineUserId,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var request = CreateDirectLineRequest(
            HttpMethod.Post,
            CreateDirectLineUri(
                "conversations/"
                    + $"{Uri.EscapeDataString(directLineConversationId)}"
                    + "/activities"),
            token);
        var json = JsonSerializer.Serialize(new
        {
            type = "message",
            from = new { id = directLineUserId },
            text = prompt,
            locale = "tr-TR"
        }, JsonSerializerOptions.Web);
        var bytes = Encoding.UTF8.GetBytes(json);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "application/json; charset=utf-8");

        using var response = await SendDirectLineAsync(
            request,
            "activity-post",
            cancellationToken,
            directLineConversationId,
            directLineUserId,
            prompt);
        EnsureSuccess(response, HttpStatusCode.OK, "activity send");
    }

    private async Task<CopilotSemanticPlanPayload> WaitForPlanAsync(
        string directLineConversationId,
        string token,
        string directLineUserId,
        CancellationToken cancellationToken)
    {
        string? watermark = null;
        while (true)
        {
            var path = "conversations/"
                + $"{Uri.EscapeDataString(directLineConversationId)}"
                + "/activities";
            if (!string.IsNullOrWhiteSpace(watermark))
            {
                path += $"?watermark={Uri.EscapeDataString(watermark)}";
            }

            using var request = CreateDirectLineRequest(
                HttpMethod.Get,
                CreateDirectLineUri(path),
                token);
            using var response = await SendDirectLineAsync(
                request,
                "activity-poll",
                cancellationToken,
                directLineConversationId,
                directLineUserId);
            EnsureSuccess(
                response,
                HttpStatusCode.OK,
                "activity retrieval");

            using var document = await ReadProtocolJsonAsync(
                response,
                cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("watermark", out var watermarkElement)
                && watermarkElement.ValueKind == JsonValueKind.String)
            {
                watermark = watermarkElement.GetString();
            }

            if (root.TryGetProperty("activities", out var activities)
                && activities.ValueKind == JsonValueKind.Array)
            {
                foreach (var activity in activities.EnumerateArray())
                {
                    if (TryGetAgentText(
                            activity,
                            directLineUserId,
                            out var text))
                    {
                        var candidate = text.Trim();
                        if (!LooksLikeSemanticPayload(candidate))
                        {
                            continue;
                        }

                        return DeserializeSemanticPayload(candidate);
                    }

                    if (activity.TryGetProperty("type", out var type)
                        && string.Equals(
                            type.GetString(),
                            "endOfConversation",
                            StringComparison.Ordinal))
                    {
                        throw new CopilotStudioPlanningException(
                            "Copilot Studio ended the conversation without a semantic plan.");
                    }
                }
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private bool TryGetAgentText(
        JsonElement activity,
        string directLineUserId,
        out string text)
    {
        text = string.Empty;
        if (!activity.TryGetProperty("type", out var type)
            || !string.Equals(
                type.GetString(),
                "message",
                StringComparison.Ordinal)
            || !activity.TryGetProperty("text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (activity.TryGetProperty("from", out var from)
            && from.ValueKind == JsonValueKind.Object)
        {
            var id = from.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var role = from.TryGetProperty("role", out var roleElement)
                ? roleElement.GetString()
                : null;
            var name = from.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.Equals(
                    id,
                    directLineUserId,
                    StringComparison.Ordinal)
                || string.Equals(
                    role,
                    "user",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(role)
                && !string.Equals(
                    role,
                    "bot",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    id,
                    _options.AgentName,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    name,
                    _options.AgentName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        text = textElement.GetString() ?? string.Empty;
        return text.Length > 0;
    }

    private static bool LooksLikeSemanticPayload(string text) =>
        text.StartsWith('{') || text.StartsWith("```", StringComparison.Ordinal);

    private static string CreateDirectLineUserId() =>
        $"dl_{Guid.NewGuid():D}";

    private static CopilotSemanticPlanPayload DeserializeSemanticPayload(
        string text)
    {
        var normalized = NormalizeMarkdownFence(text);
        if (normalized.Length > MaximumSemanticPayloadLength)
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio semantic payload exceeded the size limit.");
        }

        try
        {
            return JsonSerializer.Deserialize<CopilotSemanticPlanPayload>(
                       normalized,
                       StrictJsonOptions)
                ?? throw new CopilotStudioPlanningException(
                    "Copilot Studio returned an empty semantic payload.");
        }
        catch (JsonException exception)
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned a malformed semantic payload.",
                exception);
        }
    }

    private static string NormalizeMarkdownFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0
            || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned an invalid markdown fence.");
        }

        var language = trimmed[3..firstLineEnd].Trim();
        if (language.Length > 0
            && !string.Equals(
                language,
                "json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned an unsupported markdown fence.");
        }

        var payload = trimmed[(firstLineEnd + 1)..^3].Trim();
        if (payload.Length == 0)
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned an empty semantic payload.");
        }

        return payload;
    }

    private static void ValidatePlan(CopilotPlannedReportRequest request)
    {
        var objects = new List<object>
        {
            request,
            request.SemanticIntent,
            request.SemanticIntent.Date,
            request.SemanticIntent.Ranking,
            request.Clarification
        };
        objects.AddRange(request.SemanticIntent.Filters);
        objects.AddRange(request.UnresolvedConcepts);

        if (request.SemanticIntent.Filters.Any(item => item is null)
            || request.UnresolvedConcepts.Any(item => item is null)
            || !objects.All(IsValid))
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned an incomplete semantic payload.");
        }

        if (request.Outcome is not (
                "accepted" or "needs_clarification" or "unsupported"))
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio returned an unsupported planning outcome.");
        }
    }

    private static bool IsValid(object value) =>
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            validationResults: null,
            validateAllProperties: true);

    private static HttpRequestMessage CreateDirectLineRequest(
        HttpMethod method,
        Uri uri,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CopilotStudioPlanningException(
                "Copilot Studio did not return a Direct Line token.");
        }

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private Uri CreateDirectLineUri(string relativePath) =>
        new(
            _directLineBaseUri,
            $"{DirectLineApiPath}/{relativePath}");

    private async Task<HttpResponseMessage> SendDirectLineAsync(
        HttpRequestMessage request,
        string stage,
        CancellationToken cancellationToken,
        params string[] telemetrySensitiveValues)
    {
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Direct Line request completed. Stage: {Stage}; "
                        + "HTTP status: {HttpStatusCode}.",
                    stage,
                    (int)response.StatusCode);
            }
            else
            {
                var error = await TryReadErrorResponseAsync(
                    response,
                    cancellationToken);
                if (error is null)
                {
                    _logger.LogWarning(
                        "Direct Line request failed. Stage: {Stage}; "
                            + "HTTP status: {HttpStatusCode}; "
                            + "error code: {ErrorCode}.",
                        stage,
                        (int)response.StatusCode,
                        "error-response-unparseable");
                }
                else
                {
                    var sensitiveValues = telemetrySensitiveValues
                        .Append(_options.DirectLineSecret)
                        .Append(request.Headers.Authorization?.Parameter);
                    _logger.LogWarning(
                        "Direct Line request failed. Stage: {Stage}; "
                            + "HTTP status: {HttpStatusCode}; "
                            + "error code: {ErrorCode}; "
                            + "error message: {ErrorMessage}.",
                        stage,
                        (int)response.StatusCode,
                        SanitizeTelemetryValue(
                            error.Code,
                            sensitiveValues,
                            maximumLength: 128),
                        SanitizeTelemetryValue(
                            error.Message,
                            sensitiveValues,
                            maximumLength: 512));
                }
            }

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Direct Line request failed. Stage: {Stage}; "
                    + "exception type: {ExceptionType}.",
                stage,
                exception.GetType().Name);
            throw;
        }
    }

    private static async Task<DirectLineError?> TryReadErrorResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null
            || response.Content.Headers.ContentLength
                is > MaximumProtocolResponseLength)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            while (buffer.Length <= MaximumProtocolResponseLength)
            {
                var remaining = MaximumProtocolResponseLength
                    - (int)buffer.Length
                    + 1;
                var read = await stream.ReadAsync(
                    bytes.AsMemory(0, Math.Min(bytes.Length, remaining)),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await buffer.WriteAsync(
                    bytes.AsMemory(0, read),
                    cancellationToken);
            }

            if (buffer.Length > MaximumProtocolResponseLength)
            {
                return null;
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.String
                || !error.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(code.GetString())
                || string.IsNullOrWhiteSpace(message.GetString()))
            {
                return null;
            }

            return new DirectLineError(code.GetString()!, message.GetString()!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SanitizeTelemetryValue(
        string value,
        IEnumerable<string?> sensitiveValues,
        int maximumLength)
    {
        if (sensitiveValues.Any(sensitiveValue =>
                !string.IsNullOrEmpty(sensitiveValue)
                && value.Contains(
                    sensitiveValue,
                    StringComparison.Ordinal)))
        {
            return "redacted";
        }

        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static async Task<DirectLineConversation> ReadConversationAsync(
        HttpResponseMessage response,
        bool requireConversationId,
        CancellationToken cancellationToken)
    {
        using var document = await ReadProtocolJsonAsync(
            response,
            cancellationToken);
        var root = document.RootElement;
        var token = root.TryGetProperty("token", out var tokenElement)
            && tokenElement.ValueKind == JsonValueKind.String
                ? tokenElement.GetString()
                : null;
        var conversationId = root.TryGetProperty(
                "conversationId",
                out var conversationIdElement)
            && conversationIdElement.ValueKind == JsonValueKind.String
                ? conversationIdElement.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(token)
            || (requireConversationId
                && string.IsNullOrWhiteSpace(conversationId)))
        {
            throw new CopilotStudioPlanningException(
                "Direct Line returned an incomplete conversation response.");
        }

        return new DirectLineConversation(
            token,
            conversationId ?? string.Empty);
    }

    private static async Task<DirectLineToken> ReadTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await ReadProtocolJsonAsync(
            response,
            cancellationToken);
        var root = document.RootElement;
        var token = root.TryGetProperty("token", out var tokenElement)
            && tokenElement.ValueKind == JsonValueKind.String
                ? tokenElement.GetString()
                : null;
        var conversationId = root.TryGetProperty(
                "conversationId",
                out var conversationIdElement)
            && conversationIdElement.ValueKind == JsonValueKind.String
                ? conversationIdElement.GetString()
                : null;
        var expiresIn = 0;
        var hasExpiration = root.TryGetProperty(
                "expires_in",
                out var expirationElement)
            && expirationElement.ValueKind == JsonValueKind.Number
            && expirationElement.TryGetInt32(out expiresIn)
            && expiresIn > 0;

        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(conversationId)
            || !hasExpiration)
        {
            throw new CopilotStudioPlanningException(
                "Direct Line returned an incomplete token response.");
        }

        return new DirectLineToken(token, expiresIn, conversationId);
    }

    private static async Task<JsonDocument> ReadProtocolJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength
            is > MaximumProtocolResponseLength)
        {
            throw new CopilotStudioPlanningException(
                "Direct Line response exceeded the size limit.");
        }

        var text = await response.Content.ReadAsStringAsync(
            cancellationToken);
        if (text.Length > MaximumProtocolResponseLength)
        {
            throw new CopilotStudioPlanningException(
                "Direct Line response exceeded the size limit.");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new CopilotStudioPlanningException(
                "Direct Line returned malformed JSON.",
                exception);
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string operation) =>
        EnsureSuccess(response, [expectedStatus], operation);

    private static void EnsureSuccess(
        HttpResponseMessage response,
        IReadOnlyCollection<HttpStatusCode> expectedStatuses,
        string operation)
    {
        if (!expectedStatuses.Contains(response.StatusCode))
        {
            throw new CopilotStudioPlanningException(
                $"Direct Line {operation} failed with HTTP status "
                    + $"{(int)response.StatusCode}.");
        }
    }

    private sealed record DirectLineConversation(
        string Token,
        string ConversationId);

    private sealed record DirectLineToken(
        string Token,
        int ExpiresIn,
        string ConversationId);

    private sealed record DirectLineError(string Code, string Message);

    private sealed class CopilotSemanticPlanPayload
    {
        public required string Outcome { get; init; }

        public required CopilotSemanticIntent SemanticIntent { get; init; }

        public required IReadOnlyList<CopilotUnresolvedConcept>
            UnresolvedConcepts { get; init; }

        public required CopilotClarification Clarification { get; init; }
    }
}

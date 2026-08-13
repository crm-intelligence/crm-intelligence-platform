using System.Text.Json;
using CrmAnalytics.Api.ExceptionHandling;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Contracts.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmAnalytics.IntegrationTests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ConcurrencyConflict_ReturnsSafeConflict()
    {
        var httpContext = CreateHttpContext();
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new PersistenceConcurrencyException(
                new Exception("SQL rowversion details")),
            CancellationToken.None);
        var (body, response) = await ReadResponseAsync(httpContext);

        Assert.True(handled);
        Assert.Equal(
            StatusCodes.Status409Conflict,
            httpContext.Response.StatusCode);
        Assert.Equal(
            PersistenceConcurrencyException.ErrorCode,
            response.ErrorCode);
        Assert.Equal(
            PersistenceConcurrencyException.SafeMessage,
            response.Message);
        Assert.DoesNotContain("SQL", body);
        Assert.DoesNotContain("rowversion", body);
    }

    [Fact]
    public async Task TryHandleAsync_ForbiddenAccess_ReturnsSafeApiError()
    {
        var httpContext = CreateHttpContext();

        await CreateHandler().TryHandleAsync(
            httpContext,
            new ForbiddenAccessException(),
            CancellationToken.None);
        var (body, response) = await ReadResponseAsync(httpContext);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            httpContext.Response.StatusCode);
        Assert.Equal("ACCESS_DENIED", response.ErrorCode);
        Assert.Equal(
            ForbiddenAccessException.SafeMessage,
            response.Message);
        Assert.DoesNotContain("Report.User", body);
        Assert.DoesNotContain(
            TestDataScopeFactory.Create().UserId!,
            body);
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_ReturnsSafeInternalServerError()
    {
        var httpContext = CreateHttpContext();
        var handler = CreateHandler();
        var exception = CaptureExceptionWithStackTrace(
            "connection string and SQL details");

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);
        var (body, response) = await ReadResponseAsync(httpContext);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("UNEXPECTED_ERROR", response.ErrorCode);
        Assert.Equal(
            "İşlem sırasında beklenmeyen bir hata oluştu.",
            response.Message);
        Assert.DoesNotContain(exception.Message, body);
        Assert.DoesNotContain(exception.StackTrace!, body);
    }

    [Fact]
    public async Task TryHandleAsync_InvalidOperationException_ReturnsConflict()
    {
        var httpContext = CreateHttpContext();
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("sensitive operation details"),
            CancellationToken.None);
        var (_, response) = await ReadResponseAsync(httpContext);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("INVALID_OPERATION", response.ErrorCode);
    }

    [Fact]
    public async Task TryHandleAsync_RequestIdInRoute_IncludesRequestId()
    {
        const string requestId = "report-request-123";
        var httpContext = CreateHttpContext();
        httpContext.Request.RouteValues["requestId"] = requestId;
        var handler = CreateHandler();

        await handler.TryHandleAsync(
            httpContext,
            new KeyNotFoundException(),
            CancellationToken.None);
        var (_, response) = await ReadResponseAsync(httpContext);

        Assert.Equal(requestId, response.RequestId);
    }

    [Fact]
    public async Task TryHandleAsync_WithoutActivity_UsesNonEmptyHttpTraceIdentifier()
    {
        const string traceIdentifier = "http-trace-123";
        var httpContext = CreateHttpContext();
        httpContext.TraceIdentifier = traceIdentifier;
        var handler = CreateHandler();

        await handler.TryHandleAsync(
            httpContext,
            new TimeoutException(),
            CancellationToken.None);
        var (_, response) = await ReadResponseAsync(httpContext);

        Assert.False(string.IsNullOrWhiteSpace(response.TraceId));
        Assert.Equal(traceIdentifier, response.TraceId);
    }

    private static GlobalExceptionHandler CreateHandler()
    {
        return new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<(string Body, ApiErrorResponse Response)>
        ReadResponseAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;

        using var reader = new StreamReader(
            httpContext.Response.Body,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        var response = JsonSerializer.Deserialize<ApiErrorResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return (body, Assert.IsType<ApiErrorResponse>(response));
    }

    private static Exception CaptureExceptionWithStackTrace(string message)
    {
        try
        {
            throw new Exception(message);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}

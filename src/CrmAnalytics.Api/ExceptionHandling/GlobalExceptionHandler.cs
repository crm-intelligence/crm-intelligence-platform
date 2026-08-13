using System.Diagnostics;
using CrmAnalytics.Contracts.Common;
using CrmAnalytics.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace CrmAnalytics.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, errorCode, message) = exception switch
        {
            PersistenceConcurrencyException => (
                StatusCodes.Status409Conflict,
                PersistenceConcurrencyException.ErrorCode,
                PersistenceConcurrencyException.SafeMessage),
            ForbiddenAccessException => (
                StatusCodes.Status403Forbidden,
                ForbiddenAccessException.ErrorCode,
                ForbiddenAccessException.SafeMessage),
            DataAccessPolicyIntegrityException => (
                StatusCodes.Status500InternalServerError,
                DataAccessPolicyIntegrityException.ErrorCode,
                DataAccessPolicyIntegrityException.SafeMessage),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "INVALID_REQUEST",
                "Gönderilen istek geçersiz."),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "RESOURCE_NOT_FOUND",
                "İstenen kaynak bulunamadı."),
            InvalidOperationException => (
                StatusCodes.Status409Conflict,
                "INVALID_OPERATION",
                "İşlem mevcut durumda gerçekleştirilemez."),
            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "REQUEST_TIMEOUT",
                "İşlem zaman aşımına uğradı."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "UNEXPECTED_ERROR",
                "İşlem sırasında beklenmeyen bir hata oluştu.")
        };

        var requestId = httpContext.Request.RouteValues
            .TryGetValue("requestId", out var routeRequestId)
            ? routeRequestId?.ToString()
            : null;
        var traceId = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;

        if (exception is PersistenceConcurrencyException)
        {
            _logger.LogWarning(
                "Persistence concurrency conflict. RequestId: "
                    + "{RequestId}, TraceId: {TraceId}, "
                    + "ErrorCode: {ErrorCode}, StatusCode: {StatusCode}",
                requestId,
                traceId,
                errorCode,
                statusCode);
        }
        else if (exception is DbException or DbUpdateException)
        {
            _logger.LogError(
                "Database operation failed. RequestId: {RequestId}, "
                    + "TraceId: {TraceId}, ErrorCode: {ErrorCode}, "
                    + "StatusCode: {StatusCode}",
                requestId,
                traceId,
                errorCode,
                statusCode);
        }
        else
        {
            _logger.LogError(
                exception,
                "Request failed. RequestId: {RequestId}, "
                    + "TraceId: {TraceId}, ErrorCode: {ErrorCode}, "
                    + "StatusCode: {StatusCode}",
                requestId,
                traceId,
                errorCode,
                statusCode);
        }

        httpContext.Response.StatusCode = statusCode;

        var response = new ApiErrorResponse(
            RequestId: requestId,
            ErrorCode: errorCode,
            Message: message,
            TraceId: traceId);

        await httpContext.Response.WriteAsJsonAsync(
            response,
            cancellationToken);

        return true;
    }
}

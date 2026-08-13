using System.Security.Cryptography;
using System.Text;
using CrmAnalytics.Application.Teams;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/internal")]
public sealed class InternalTeamsController(
    ITeamsNotificationTargetStore targets,
    ITeamsCardActionSubmissionStore actions,
    IOptions<TeamsNotificationOptions> options,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPut("teams-targets/{requestId}")]
    public async Task<IActionResult> PutTarget(
        string requestId,
        [FromBody] RegisterTeamsTargetRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey() || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Unauthorized();
        if (!IsIdentifier(requestId, 32)
            || string.IsNullOrWhiteSpace(request.ConversationId)
            || request.ConversationId.Trim().Length > 512)
            return BadRequest();

        try
        {
            await targets.UpsertAsync(requestId.Trim(),
                request.ConversationId.Trim(), timeProvider.GetUtcNow(),
                cancellationToken);
            return NoContent();
        }
        catch (TeamsNotificationTargetConflictException)
        {
            return Conflict(new { errorCode = "TEAMS_TARGET_CONFLICT" });
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
    }

    [HttpGet("teams-targets/{requestId}")]
    public async Task<IActionResult> GetTarget(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey() || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Unauthorized();
        if (!IsIdentifier(requestId, 32)) return BadRequest();
        var target = await targets.FindAsync(requestId.Trim(), cancellationToken);
        return target is null
            ? NotFound()
            : Ok(new TeamsTargetResponse
            {
                RequestId = target.RequestId,
                ConversationId = target.ConversationId
            });
    }

    [HttpPost("teams-actions/{actionToken}/claim")]
    public async Task<IActionResult> ClaimAction(
        string actionToken,
        [FromBody] ClaimTeamsActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey() || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Unauthorized();
        if (!IsIdentifier(actionToken, 64)
            || !IsIdentifier(request.RequestId, 32)
            || !IsIdentifier(request.ActionType, 64)
            || !IsIdentifier(request.LockOwner, 256)) return BadRequest();
        try
        {
            var claim = await actions.ClaimAsync(actionToken.Trim(),
                request.RequestId.Trim(), request.ActionType.Trim(),
                request.LockOwner.Trim(), timeProvider.GetUtcNow(),
                TimeSpan.FromSeconds(60), cancellationToken);
            return Ok(new ClaimTeamsActionResponse
            {
                Result = claim.Result.ToString(),
                ResultRequestId = claim.ResultRequestId
            });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { errorCode = "TEAMS_ACTION_CONFLICT" });
        }
    }

    [HttpPost("teams-actions/{actionToken}/complete")]
    public async Task<IActionResult> CompleteAction(
        string actionToken,
        [FromBody] CompleteTeamsActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey() || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Unauthorized();
        if (!IsIdentifier(actionToken, 64)
            || !IsIdentifier(request.LockOwner, 256)
            || (request.ResultRequestId is not null
                && !IsIdentifier(request.ResultRequestId, 32))) return BadRequest();
        try
        {
            await actions.CompleteAsync(actionToken.Trim(),
                request.LockOwner.Trim(), request.ResultRequestId?.Trim(),
                timeProvider.GetUtcNow(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException) { return Conflict(); }
    }

    [HttpPost("teams-actions/{actionToken}/release")]
    public async Task<IActionResult> ReleaseAction(
        string actionToken,
        [FromBody] ReleaseTeamsActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey() || string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Unauthorized();
        if (!IsIdentifier(actionToken, 64)
            || !IsIdentifier(request.LockOwner, 256)) return BadRequest();
        try
        {
            await actions.ReleaseAsync(actionToken.Trim(),
                request.LockOwner.Trim(), timeProvider.GetUtcNow(),
                cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException) { return Conflict(); }
    }

    private bool HasValidApiKey()
    {
        if (!Request.Headers.TryGetValue(
                TeamsInternalHttpConstants.ApiKeyHeaderName, out var supplied)
            || supplied.Count != 1) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(
            supplied[0] ?? string.Empty));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(
            options.Value.ApiKey ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool IsIdentifier(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Length > maxLength) return false;
        return value.Trim().All(c => char.IsAsciiLetterOrDigit(c)
            || c is '-' or '_' or ':' or '.');
    }
}

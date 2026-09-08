using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Api.Security;
using Meridian.Application.Abstractions;
using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Api.Filters;

public sealed class IdempotentAttribute : ServiceFilterAttribute
{
    public IdempotentAttribute() : base(typeof(IdempotencyFilter)) { }
}

public sealed class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const string RetryAfterHeaderValue = "1";

    private readonly IIdempotencyRepository _records;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(
        IIdempotencyRepository records,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<JsonOptions> jsonOptions,
        ILogger<IdempotencyFilter> logger)
    {
        _records = records;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _serializerOptions = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing idempotency key",
                Detail = $"The {HeaderName} header is required for this endpoint.",
            });
            return;
        }

        var key = headerValues.ToString();
        if (key.Length > IdempotencyRecord.MaxKeyLength)
        {
            context.Result = Problem(
                StatusCodes.Status422UnprocessableEntity, "invalid-idempotency-key", "Invalid idempotency key",
                $"The {HeaderName} header must be at most {IdempotencyRecord.MaxKeyLength} characters.");
            return;
        }

        var userId = context.HttpContext.User.GetUserId();
        var requestHash = ComputeRequestHash(context);
        var cancellationToken = context.HttpContext.RequestAborted;

        var existing = await _records.FindAsync(userId, key, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsCompleted)
            {
                SetInFlightResult(context);
                return;
            }

            context.Result = existing.RequestHash != requestHash
                ? Problem(
                    StatusCodes.Status422UnprocessableEntity, "idempotency-key-conflict", "Idempotency key conflict",
                    "This idempotency key was already used with a different payload.")
                : new ContentResult
                {
                    Content = existing.ResponseBody,
                    ContentType = "application/json",
                    StatusCode = existing.ResponseStatusCode,
                };
            return;
        }

        var reservation = IdempotencyRecord.Reserve(key, userId, requestHash, _clock.UtcNow);
        _records.Add(reservation);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            _unitOfWork.ClearTracking();
            SetInFlightResult(context);
            return;
        }

        var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            await transaction.DisposeAsync();
            await RemoveReservationAsync(reservation, cancellationToken);
            return;
        }

        var success = ExtractSuccess(executed.Result);
        if (success is null)
        {
            await transaction.DisposeAsync();
            await RemoveReservationAsync(reservation, cancellationToken);
            return;
        }

        reservation.Complete(success.Value.StatusCode, success.Value.Body, _clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
    }

    private async Task RemoveReservationAsync(IdempotencyRecord reservation, CancellationToken cancellationToken)
    {
        // The action may have exhausted its own ConcurrencyRetry budget and thrown with
        // stale-versioned entities still tracked (the retry loop only clears tracking
        // between attempts, not on the final failure). Clear first so this delete is the
        // only pending change, and never let a failure here escape and replace the
        // caller's real exception — losing the reservation cleanup just means the key
        // stays in-flight a little longer, which is recoverable; masking the original
        // error is not.
        _unitOfWork.ClearTracking();
        _records.Remove(reservation);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to release idempotency reservation {Key} for user {UserId}; it stays in-flight.",
                reservation.Key, reservation.UserId);
        }
    }

    private static void SetInFlightResult(ActionExecutingContext context)
    {
        context.HttpContext.Response.Headers["Retry-After"] = RetryAfterHeaderValue;
        context.Result = Problem(
            StatusCodes.Status409Conflict, "idempotency-in-flight", "Idempotency key in flight",
            "A request with this idempotency key is already being processed. Retry shortly.");
    }

    private static ObjectResult Problem(int status, string type, string title, string detail) => new(
        new ProblemDetails { Status = status, Type = type, Title = title, Detail = detail })
    {
        StatusCode = status,
    };

    private (int StatusCode, string Body)? ExtractSuccess(IActionResult? result)
    {
        if (result is not IStatusCodeActionResult { StatusCode: { } statusCode } || statusCode is < 200 or >= 300)
            return null;

        var value = (result as ObjectResult)?.Value;
        var body = value is null ? string.Empty : JsonSerializer.Serialize(value, _serializerOptions);
        return (statusCode, body);
    }

    private string ComputeRequestHash(ActionExecutingContext context)
    {
        var canonical = JsonSerializer.Serialize(
            context.ActionArguments
                .Where(a => a.Value is not CancellationToken)
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .ToDictionary(a => a.Key, a => a.Value),
            _serializerOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

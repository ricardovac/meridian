using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Api.Security;
using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Meridian.Api.Filters;

public sealed class IdempotentAttribute : ServiceFilterAttribute
{
    public IdempotentAttribute() : base(typeof(IdempotencyFilter)) { }
}

public sealed class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    private readonly IIdempotencyRepository _records;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly JsonSerializerOptions _serializerOptions;

    public IdempotencyFilter(
        IIdempotencyRepository records,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<JsonOptions> jsonOptions)
    {
        _records = records;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _serializerOptions = jsonOptions.Value.JsonSerializerOptions;
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
        var userId = context.HttpContext.User.GetUserId();
        var requestHash = ComputeRequestHash(context);

        var existing = await _records.FindAsync(userId, key, context.HttpContext.RequestAborted);
        if (existing is not null)
        {
            context.Result = existing.RequestHash == requestHash
                ? new ContentResult
                {
                    Content = existing.ResponseBody,
                    ContentType = "application/json",
                    StatusCode = existing.ResponseStatusCode,
                }
                : new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Type = "idempotency-key-conflict",
                    Title = "Idempotency key conflict",
                    Detail = "This idempotency key was already used with a different payload.",
                })
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity,
                };
            return;
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(context.HttpContext.RequestAborted);
        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
            return;

        if (executed.Result is ObjectResult { Value: not null } objectResult &&
            objectResult.StatusCode is >= 200 and < 300)
        {
            var body = JsonSerializer.Serialize(objectResult.Value, _serializerOptions);
            _records.Add(IdempotencyRecord.Create(
                key, userId, requestHash, objectResult.StatusCode.Value, body, _clock.UtcNow));

            await _unitOfWork.SaveChangesAsync(context.HttpContext.RequestAborted);
            await transaction.CommitAsync(context.HttpContext.RequestAborted);
        }
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

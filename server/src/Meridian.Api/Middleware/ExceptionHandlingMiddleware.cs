using Meridian.Application.Exceptions;
using Meridian.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var problem = MapToProblem(ex);
            if (problem.Status >= 500)
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}.",
                    context.Request.Method, context.Request.Path);

            context.Response.StatusCode = problem.Status!.Value;
            await context.Response.WriteAsJsonAsync(
                problem, options: null, contentType: "application/problem+json");
        }
    }

    private static ProblemDetails MapToProblem(Exception exception) => exception switch
    {
        InsufficientFundsException ex => Problem(
            StatusCodes.Status422UnprocessableEntity, "insufficient-funds", "Insufficient funds", ex.Message),
        CurrencyMismatchException ex => Problem(
            StatusCodes.Status422UnprocessableEntity, "currency-mismatch", "Currency mismatch", ex.Message),
        InvalidAmountScaleException ex => Problem(
            StatusCodes.Status422UnprocessableEntity, "invalid-amount-scale", "Invalid amount scale", ex.Message),
        DomainValidationException ex => Problem(
            StatusCodes.Status400BadRequest, "validation-error", "Validation failed", ex.Message),
        RequestValidationException ex => Problem(
            StatusCodes.Status400BadRequest, "validation-error", "Validation failed", ex.Message),
        NotFoundException ex => Problem(
            StatusCodes.Status404NotFound, "not-found", "Resource not found", ex.Message),
        ConflictException ex => Problem(
            StatusCodes.Status409Conflict, "conflict", "Conflict", ex.Message),
        InvalidCredentialsException ex => Problem(
            StatusCodes.Status401Unauthorized, "invalid-credentials", "Unauthorized", ex.Message),
        ConcurrencyConflictException ex => Problem(
            StatusCodes.Status409Conflict, "concurrency-conflict", "Concurrency conflict", ex.Message),
        _ => Problem(
            StatusCodes.Status500InternalServerError, "internal-error", "An unexpected error occurred",
            "The server encountered an unexpected condition."),
    };

    private static ProblemDetails Problem(int status, string type, string title, string detail) => new()
    {
        Status = status,
        Type = type,
        Title = title,
        Detail = detail,
    };
}

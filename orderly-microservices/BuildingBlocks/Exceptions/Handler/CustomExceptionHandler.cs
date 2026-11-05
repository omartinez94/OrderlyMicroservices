using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace BuildingBlocks.Exceptions.Handler;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError("Error message: {error}, time of occurance: {time}", exception.Message, SystemClock.Instance.GetCurrentInstant());

        (string Detail, string Title, int StatusCode) = exception switch
        {
            InternalServerException => (
                exception.Message ?? "An unexpected error occurred.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError
            ),
            BadHttpRequestException => (
                exception.Message ?? "Bad request.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            BadRequestException => (
                exception.Message ?? "Bad request.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            NotFoundException => (
                exception.Message ?? "The requested resource was not found.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound
            ),
            ValidationException => (
                exception.Message ?? "Validation failed.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            ForbiddenException => (
                exception.Message ?? "Forbidden.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden
            ),
            _ when IsStateTransitionException(exception) => (
                exception.Message ?? "The requested transition is not permitted from the current state.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict
            ),
            _ when IsActivityInvariantException(exception) => (
                exception.Message ?? "The activity payload is invalid.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity
            ),
            _ when IsConflictException(exception) => (
                exception.Message ?? "Conflict.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict
            ),
            _ when IsUnprocessableException(exception) => (
                exception.Message ?? "Unprocessable entity.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity
            ),
            _ when IsServiceUnavailableException(exception) => (
                exception.Message ?? "Service unavailable.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable
            ),
            _ => (
                exception.Message ?? "An unexpected error occurred.",
                exception.GetType().Name ?? "Internal Server Error.",
                StatusCodes.Status500InternalServerError
            )
        };

        ProblemDetails? problemDetails = new ProblemDetails
        {
            Title = Title,
            Status = StatusCode,
            Detail = Detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions.Add("traceId", httpContext.TraceIdentifier);

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions.Add("ValidationErrors", validationException.Errors);
        }
        else if (IsStateTransitionException(exception))
        {
            // Reflect the optional FromStatus / AttemptedTransition
            // properties without a hard reference to the Domain assemblies
            // (BuildingBlocks must stay Domain-agnostic).
            object? fromStatus = TryGetProperty(exception, "FromStatus");
            object? attempted = TryGetProperty(exception, "AttemptedTransition");
            if (fromStatus is not null)
            {
                problemDetails.Extensions["fromStatus"] = fromStatus;
            }
            if (attempted is not null)
            {
                problemDetails.Extensions["attemptedTransition"] = attempted;
            }
        }
        else if (IsConflictException(exception))
        {
            object? unavailableItemIds = TryGetProperty(exception, "UnavailableItemIds");
            if (unavailableItemIds is not null)
            {
                problemDetails.Extensions["unavailableItemIds"] = unavailableItemIds;
            }
        }
        else if (IsServiceUnavailableException(exception))
        {
            object? retryAfter = TryGetProperty(exception, "RetryAfterSeconds");
            if (retryAfter is int seconds)
            {
                httpContext.Response.Headers.RetryAfter = seconds.ToString();
            }
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    /// <summary>
    /// Matches concrete exceptions whose simple name ends with
    /// <c>StateTransitionException</c> (e.g.
    /// <c>InvalidOrderStateTransitionException</c>,
    /// <c>InvalidKitchenTicketStateTransitionException</c>). BuildingBlocks
    /// stays Domain-agnostic by matching on naming convention rather than
    /// referencing the per-service exception types.
    /// </summary>
    private static bool IsStateTransitionException(Exception exception) =>
        exception.GetType().Name.EndsWith("StateTransitionException", StringComparison.Ordinal);

    /// <summary>
    /// Matches <c>OrderActivityInvariantException</c> (and any future
    /// activity-feed invariant exception). Surfaces a 422 because the
    /// request is well-formed but the activity payload cannot be
    /// persisted as-is — the caller must reshape it (e.g. truncate
    /// notes, drop the unknown enum value).
    /// </summary>
    private static bool IsActivityInvariantException(Exception exception) =>
        exception.GetType().Name.EndsWith("ActivityInvariantException", StringComparison.Ordinal);

    private static bool IsConflictException(Exception exception) =>
        exception.GetType().Name.EndsWith("ConflictException", StringComparison.Ordinal);

    private static bool IsUnprocessableException(Exception exception) =>
        exception.GetType().Name.EndsWith("UnprocessableException", StringComparison.Ordinal) ||
        exception.GetType().Name.EndsWith("UnprocessableEntityException", StringComparison.Ordinal);

    private static bool IsServiceUnavailableException(Exception exception) =>
        exception.GetType().Name.EndsWith("UnavailableException", StringComparison.Ordinal);

    private static object? TryGetProperty(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
}

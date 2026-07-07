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
            _ when IsStateTransitionException(exception) => (
                exception.Message ?? "The requested transition is not permitted from the current state.",
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict
            ),
            _ => (
                exception.Message ?? "An unexpected error occurred.",
                exception.GetType().Name ?? "Internal Server Error.",
                StatusCodes.Status500InternalServerError
            )
        };

        var problemDetails = new ProblemDetails
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
            var fromStatus = TryGetProperty(exception, "FromStatus");
            var attempted = TryGetProperty(exception, "AttemptedTransition");
            if (fromStatus is not null) problemDetails.Extensions["fromStatus"] = fromStatus;
            if (attempted is not null) problemDetails.Extensions["attemptedTransition"] = attempted;
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

    private static object? TryGetProperty(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
}

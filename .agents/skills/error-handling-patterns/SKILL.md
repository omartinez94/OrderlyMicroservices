---
name: error-handling-patterns
description: Master error handling patterns in C# .NET including Global Exception Handling, Problem Details, Result types, and resilience with Polly to build fault-tolerant microservices.
---

# Error Handling Patterns in .NET

Build resilient .NET applications with robust error handling strategies that gracefully handle failures, provide excellent debugging experiences, and return standardized responses using Problem Details.

## When to Use This Skill

- Implementing error handling in new ASP.NET Core features
- Designing error-resilient APIs using Carter and MediatR
- Creating global exception handlers middleware
- Returning standardized ProblemDetails responses
- Implementing retry, circuit breaker, and timeout policies (Polly)
- Building fault-tolerant microservices

## Core Concepts

### 1. Error Handling Philosophies in .NET

**Exceptions vs Result Types:**

- **Exceptions**: Use for exceptional, unexpected situations (e.g., database down, missing configuration, null reference).
- **Result Types / Domain Errors**: Use for expected business logic failures (e.g., validation failed, resource not found, insufficient funds).

**Standardized API Responses:**
- Always use `ProblemDetails` for API error responses (RFC 7807 standard).

## C# .NET Specific Patterns

### Pattern 1: Custom Exception Hierarchy

Create specific exceptions for your domain that can be caught by global handlers.

```csharp
namespace BuildingBlocks.Exceptions;

public class CustomException : Exception
{
    public CustomException(string message) : base(message)
    {
    }

    public CustomException(string message, string details) : base(message)
    {
        Details = details;
    }

    public string? Details { get; }
}

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message)
    {
    }

    public BadRequestException(string message, string details) : base(message)
    {
        Details = details;
    }
    
    public string? Details { get; }
}

public class NotFoundException : Exception
{
    public NotFoundException(string name, object key) 
        : base($"Entity \"{name}\" ({key}) was not found.")
    {
    }
}
```

### Pattern 2: Global Exception Handling (ASP.NET Core 8+)

Use the `IExceptionHandler` interface to globally handle exceptions and map them to standard `ProblemDetails`.

```csharp
using BuildingBlocks.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Exceptions.Handler;

public class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, 
        Exception exception, 
        CancellationToken cancellationToken)
    {
        logger.LogError(
            "Error Message: {exceptionMessage}, Time of occurrence {time}", 
            exception.Message, DateTime.UtcNow);

        (string Detail, string Title, int StatusCode) details = exception switch
        {
            InternalServerException => (
                exception.Message,
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError
            ),
            ValidationException ve => (
                ve.Message,
                ve.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            BadRequestException => (
                exception.Message,
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest
            ),
            NotFoundException => (
                exception.Message,
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound
            ),
            _ => (
                exception.Message,
                exception.GetType().Name,
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError
            )
        };

        var problemDetails = new ProblemDetails
        {
            Title = details.Title,
            Detail = details.Detail,
            Status = details.StatusCode,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions.Add("traceId", httpContext.TraceIdentifier);

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions.Add("ValidationErrors", validationException.Errors);
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken);
        return true;
    }
}
```

**Register in Program.cs:**
```csharp
builder.Services.AddExceptionHandler<CustomExceptionHandler>();
// ...
app.UseExceptionHandler(options => { });
```

### Pattern 3: FluentValidation in MediatR Pipeline

Instead of throwing exceptions from controllers, intercept invalid commands using MediatR pipeline behaviors.

```csharp
using FluentValidation;
using MediatR;
using ValidationException = BuildingBlocks.Exceptions.ValidationException;

namespace BuildingBlocks.Behaviors;

public class ValidationBehavior<TRequest, TResponse> 
    (IEnumerable<IValidator<TRequest>> validators) 
    : IPipelineBehavior<TRequest, TResponse> 
    where TRequest : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .Where(r => r.Errors.Any())
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Any())
            throw new ValidationException(failures);

        return await next();
    }
}
```

### Pattern 4: Resilience with Polly (Circuit Breaker & Retry)

Use Polly for resilience in HTTP clients or asynchronous operations.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Catalog.API;

public static class HttpClientExtensions
{
    public static IServiceCollection AddHttpClientsWithResilience(this IServiceCollection services)
    {
        services.AddHttpClient("CatalogClient", client =>
        {
            client.BaseAddress = new Uri("https://api.example.com");
        })
        .AddTransientHttpErrorPolicy(policyBuilder =>
            policyBuilder.WaitAndRetryAsync(
                3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
        .AddTransientHttpErrorPolicy(policyBuilder =>
            policyBuilder.CircuitBreakerAsync(
                5, TimeSpan.FromSeconds(30)));

        return services;
    }
}
```

### Pattern 5: Result Pattern (Domain Errors)

For expected failures, returning a `Result` type avoids exception overhead.

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    protected Result(bool isSuccess, T? value, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string errorMessage) => new(false, default, errorMessage);
}

// Usage in Handler
public async Task<Result<Order>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
{
    var customer = await repository.GetCustomerAsync(request.CustomerId);
    if (customer == null)
    {
        return Result<Order>.Failure("Customer not found.");
    }
    
    // Process order...
    return Result<Order>.Success(order);
}
```

## Best Practices

1. **Fail Fast**: Validate input early using MediatR Behaviors and FluentValidation.
2. **Preserve Context**: Never use `throw ex;`. Always use `throw;` to preserve the stack trace, or wrap it `throw new CustomException("...", ex);`.
3. **Use Problem Details**: ASP.NET Core makes this easy; use it to standardize your API errors.
4. **Log Appropriately**: Log at the global exception handler level. Don't scatter `try/catch` and logging throughout business logic.
5. **Handle at Right Level**: Catch where you can meaningfully handle (e.g., fallback data). Otherwise, let it bubble up to the global handler.
6. **Polly for External Calls**: Always configure retry and circuit breaker policies when calling other microservices or third-party APIs.

## Common Pitfalls

- **Catching Too Broadly**: `catch (Exception)` inside business logic hides bugs. Let it propagate.
- **Empty Catch Blocks**: Silently swallowing errors is dangerous.
- **Logging and Re-throwing**: `catch(Exception e) { _logger.LogError(e); throw; }` creates duplicate log entries if done at multiple layers.
- **Not Awaiting Tasks properly**: If you don't await a Task, exceptions inside it might be silently swallowed or crash the application ungracefully.
- **Returning Error Codes (200 OK with Error Object)**: Always map errors to correct HTTP Status Codes (400, 404, 500) using `ProblemDetails`.

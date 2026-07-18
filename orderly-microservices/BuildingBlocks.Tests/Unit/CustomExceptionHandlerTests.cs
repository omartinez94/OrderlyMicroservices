using System.Text.Json;
using BuildingBlocks.Exceptions;
using BuildingBlocks.Exceptions.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Tests.Unit;

/// <summary>
/// Pins the exception → HTTP status code mapping in
/// <see cref="CustomExceptionHandler"/>. Phase-1 of the Basket plan adds the
/// <see cref="ForbiddenException"/> → 403 arm; this test pins the behaviour
/// so a future refactor of the switch expression can't silently regress it.
/// </summary>
public sealed class CustomExceptionHandlerTests
{
    [Fact]
    public async Task ForbiddenException_MapsTo403()
    {
        var logger = Substitute.For<ILogger<CustomExceptionHandler>>();
        var handler = new CustomExceptionHandler(logger);
        var httpContext = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };
        var ex = new ForbiddenException("Cannot read basket for other user.");

        var handled = await handler.TryHandleAsync(httpContext, ex, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        httpContext.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            CancellationToken.None);

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Title.Should().Be(nameof(ForbiddenException));
        problem.Detail.Should().Be("Cannot read basket for other user.");
    }

    [Fact]
    public async Task ForbiddenException_DefaultMessage_MapsTo403()
    {
        var logger = Substitute.For<ILogger<CustomExceptionHandler>>();
        var handler = new CustomExceptionHandler(logger);
        var httpContext = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };

        await handler.TryHandleAsync(httpContext, new ForbiddenException(), CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        httpContext.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            CancellationToken.None);

        problem.Should().NotBeNull();
        problem!.Detail.Should().Be("Forbidden.");
    }
}
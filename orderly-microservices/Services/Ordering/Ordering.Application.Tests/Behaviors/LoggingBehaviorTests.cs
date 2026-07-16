using BuildingBlocks.Behaviors;
using BuildingBlocks.Correlation;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Ordering.Application.Tests.Behaviors;

/// <summary>
/// Covers <see cref="LoggingBehavior{TRequest, TResponse}"/>: the
/// correlation-id contract that underpins the <c>OrderActivity</c> feed.
/// <list type="bullet">
/// <item>The header value is propagated to <see cref="CorrelationContext.Current"/>
/// during the handler scope.</item>
/// <item>A fresh <see cref="Guid"/> is generated when the header is missing
/// or empty.</item>
/// <item>The ambient is cleared in <c>finally</c> — both on success and on
/// exception — so the <see cref="AsyncLocal{T}"/> cannot leak across requests
/// on the same logical call context.</item>
/// <item>Outside an HTTP scope the ambient stays <c>null</c> — no
/// <c>Set</c> happens, so a background worker's pre-existing value is
/// untouched.</item>
/// </list>
/// </summary>
public sealed class LoggingBehaviorTests
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public async Task Handle_HeaderPresent_SetsAmbientToHeaderValue()
    {
        var accessor = NewHttpContextAccessor(headerValue: "abc-123");

        string? observed = null;
        var behavior = new LoggingBehavior<TestRequest, TestResponse>(
            NullLogger<LoggingBehavior<TestRequest, TestResponse>>.Instance,
            accessor);
        var next = (RequestHandlerDelegate<TestResponse>)(ct =>
        {
            observed = CorrelationContext.Current;
            return Task.FromResult(new TestResponse());
        });

        await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        observed.Should().Be("abc-123");
        CorrelationContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task Handle_HeaderMissing_GeneratesGuid()
    {
        var accessor = NewHttpContextAccessor(headerValue: null);

        string? observed = null;
        var behavior = new LoggingBehavior<TestRequest, TestResponse>(
            NullLogger<LoggingBehavior<TestRequest, TestResponse>>.Instance,
            accessor);
        var next = (RequestHandlerDelegate<TestResponse>)(ct =>
        {
            observed = CorrelationContext.Current;
            return Task.FromResult(new TestResponse());
        });

        await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        observed.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(observed, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_HeaderWhitespace_GeneratesGuid()
    {
        var accessor = NewHttpContextAccessor(headerValue: "   ");

        string? observed = null;
        var behavior = new LoggingBehavior<TestRequest, TestResponse>(
            NullLogger<LoggingBehavior<TestRequest, TestResponse>>.Instance,
            accessor);

        await behavior.Handle(
            new TestRequest(),
            _ =>
            {
                observed = CorrelationContext.Current;
                return Task.FromResult(new TestResponse());
            },
            CancellationToken.None);

        Guid.TryParse(observed, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_HandlerThrows_ClearsAmbient()
    {
        var accessor = NewHttpContextAccessor(headerValue: "boom");

        var behavior = new LoggingBehavior<TestRequest, TestResponse>(
            NullLogger<LoggingBehavior<TestRequest, TestResponse>>.Instance,
            accessor);

        Func<Task> act = () => behavior.Handle(
            new TestRequest(),
            _ => throw new InvalidOperationException("handler fault"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        CorrelationContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoHttpContext_LeavesAmbientUntouched()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var behavior = new LoggingBehavior<TestRequest, TestResponse>(
            NullLogger<LoggingBehavior<TestRequest, TestResponse>>.Instance,
            accessor);

        string? observed = "<not-set>";
        await behavior.Handle(
            new TestRequest(),
            _ =>
            {
                observed = CorrelationContext.Current;
                return Task.FromResult(new TestResponse());
            },
            CancellationToken.None);

        observed.Should().BeNull();
        CorrelationContext.Current.Should().BeNull();
    }

    private static IHttpContextAccessor NewHttpContextAccessor(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
        {
            httpContext.Request.Headers[CorrelationHeader] = headerValue;
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private sealed record TestRequest : IRequest<TestResponse>;

    private sealed record TestResponse;
}
namespace Basket.API.Tests.Unit;

/// <summary>
/// Locks the Phase-1 redaction rule from the Basket plan §0.3:
/// when a request type carries <see cref="PciSensitiveAttribute"/>,
/// <see cref="LoggingBehavior{TRequest,TResponse}"/> replaces the
/// payload in every log line with the type name + " (payload redacted)".
/// </summary>
public sealed class LoggingBehaviorRedactionTests
{
    [Fact]
    public async Task PciSensitiveCommand_PayloadIsRedactedInLogs()
    {
        var logger = new RecordingLogger<LoggingBehavior<PciSensitiveCommand, object>>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        var behavior = new LoggingBehavior<PciSensitiveCommand, object>(logger, accessor);

        await behavior.Handle(
            new PciSensitiveCommand(new BasketCheckoutDto
            {
                UserId = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                CardNumber = "4111-1111-1111-1111",
            }),
            _ => Task.FromResult(new object()),
            CancellationToken.None);

        // The redaction marker appears at least once (start + end lines).
        logger.Calls
            .Select(c => c.Formatted)
            .Should()
            .Contain(m => m.Contains("payload redacted", StringComparison.Ordinal));

        // The card number never reaches a log line.
        logger.Calls
            .Select(c => c.Formatted)
            .Should()
            .NotContain(m => m.Contains("4111-1111-1111-1111", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonSensitiveCommand_PayloadIsLogged()
    {
        var logger = new RecordingLogger<LoggingBehavior<NonSensitiveCommand, object>>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        var behavior = new LoggingBehavior<NonSensitiveCommand, object>(logger, accessor);

        await behavior.Handle(
            new NonSensitiveCommand("hello"),
            _ => Task.FromResult(new object()),
            CancellationToken.None);

        logger.Calls
            .Select(c => c.Formatted)
            .Should()
            .Contain(m => m.Contains("hello", StringComparison.Ordinal));
    }

    [PciSensitive]
    public sealed record PciSensitiveCommand(BasketCheckoutDto BasketCheckoutDto)
        : IRequest<object>;

    public sealed record NonSensitiveCommand(string Value) : IRequest<object>;
}

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that records every log call so tests
/// can assert on the formatted output. Avoids the Castle DynamicProxy
/// generic-arity pain that surfaces when NSubstitute mocks
/// <c>ILogger&lt;ILoggingBehavior&lt;TRequest, TResponse&gt;&gt;</c>
/// across closed-generic command types.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, object? State, string Formatted)> Calls { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Calls.Add((logLevel, state, formatter(state, exception)));
    }

    private sealed class Scope : IDisposable
    {
        public static readonly Scope Instance = new();
        public void Dispose() { }
    }
}
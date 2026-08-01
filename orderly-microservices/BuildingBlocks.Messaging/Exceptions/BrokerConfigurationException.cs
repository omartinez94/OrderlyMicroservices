namespace BuildingBlocks.Messaging.Exceptions;

/// <summary>
/// Thrown at startup when <see cref="BuildingBlocks.Messaging.MassTransit.Extensions.AddMessageBroker"/>
/// finds required <c>MessageBroker</c> configuration keys missing. The
/// exception's <see cref="MissingKeys"/> list enumerates every absent key so
/// the operator sees the full gap at once instead of one null-deref at a time.
/// </summary>
/// <remarks>
/// Mirrors <see cref="BuildingBlocks.Exceptions.NotFoundException"/> — same
/// two-constructor pattern (message-only; message + structured payload).
/// Lives in <c>BuildingBlocks.Messaging.Exceptions</c> rather than the
/// generic <c>BuildingBlocks.Exceptions</c> namespace so MassTransit
/// reference code doesn't drag in the wider exceptions tree.
/// </remarks>
public class BrokerConfigurationException : Exception
{
    public IReadOnlyList<string> MissingKeys { get; }

    public BrokerConfigurationException(string message)
        : base(message)
    {
        MissingKeys = Array.Empty<string>();
    }

    public BrokerConfigurationException(string message, IReadOnlyList<string> missingKeys)
        : base(message)
    {
        MissingKeys = missingKeys;
    }
}
using System.Diagnostics.Metrics;

namespace Ordering.API.Observability;

public sealed class OrderingLiveMetrics : IDisposable
{
    public const string MeterName = "Orderly.Ordering";

    private readonly Meter _meter;
    private readonly UpDownCounter<long> _connectedClients;
    private readonly Counter<long> _pushes;
    private readonly Counter<long> _pushFailures;
    private readonly Histogram<double> _pushLatencyMs;

    public OrderingLiveMetrics()
    {
        _meter = new Meter(MeterName);
        _connectedClients = _meter.CreateUpDownCounter<long>(
            "ordering.live.hub.clients",
            unit: "{clients}",
            description: "Number of active OrdersHub connections.");
        _pushes = _meter.CreateCounter<long>(
            "ordering.live.pushes",
            unit: "{pushes}",
            description: "Total real-time order and stats pushes emitted by OrdersHub.");
        _pushFailures = _meter.CreateCounter<long>(
            "ordering.live.push.failures",
            unit: "{failures}",
            description: "Total failed real-time pushes on OrdersHub.");
        _pushLatencyMs = _meter.CreateHistogram<double>(
            "ordering.live.push.latency_ms",
            unit: "ms",
            description: "Latency in milliseconds from aggregate commit to OrdersHub send.");
    }

    public void OnClientConnected() => _connectedClients.Add(1);

    public void OnClientDisconnected() => _connectedClients.Add(-1);

    public void RecordPush(string changeType, double latencyMs)
    {
        KeyValuePair<string, object?> tag = new("change_type", changeType);
        _pushes.Add(1, tag);
        if (latencyMs >= 0)
        {
            _pushLatencyMs.Record(latencyMs, tag);
        }
    }

    public void RecordPushFailure(string reason)
    {
        _pushFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Observability.Tests.Integration;

/// <summary>
/// Minimal in-process OTLP HTTP receiver used by
/// <see cref="OrderlyOpenTelemetryTests"/>. Buffers the most-recent
/// POST body bytes per signal (<c>/v1/traces</c>, <c>/v1/metrics</c>,
/// <c>/v1/logs</c>) and returns 200 OK. Listens on a kernel-assigned
/// free port via <c>ListenAnyIP(0)</c>.
/// </summary>
public sealed class FakeOtlpReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;
    private bool _disposed;

    /// <summary>POST bodies received at <c>/v1/traces</c> (one entry per request).</summary>
    public ConcurrentQueue<byte[]> Traces { get; } = new();

    /// <summary>POST bodies received at <c>/v1/metrics</c> (one entry per request).</summary>
    public ConcurrentQueue<byte[]> Metrics { get; } = new();

    /// <summary>POST bodies received at <c>/v1/logs</c> (one entry per request).</summary>
    public ConcurrentQueue<byte[]> Logs { get; } = new();

    /// <summary>The TCP port the receiver is listening on (kernel-assigned free port).</summary>
    public int Port { get; }

    public FakeOtlpReceiver()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
        });
        builder.Logging.ClearProviders();
        // Suppress the receiver's own log output so the test
        // console doesn't drown in noise. The receiver still
        // records POST bodies into the Traces / Metrics / Logs
        // ConcurrentQueues.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        _app = builder.Build();

        _app.MapPost("/v1/traces", async (HttpRequest request) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            Traces.Enqueue(ms.ToArray());
            return Results.Ok();
        });

        _app.MapPost("/v1/metrics", async (HttpRequest request) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            Metrics.Enqueue(ms.ToArray());
            return Results.Ok();
        });

        _app.MapPost("/v1/logs", async (HttpRequest request) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            Logs.Enqueue(ms.ToArray());
            return Results.Ok();
        });

        // Start the host synchronously so the Port property is available
        // before the caller reads it. CreateBuilder() configures Kestrel
        // but the listener doesn't bind until StartAsync runs.
        _app.Start();

        // Resolve the bound port from the address the server is listening on.
        var server = _app.Services.GetRequiredService<IServer>();
        var addressFeature = server.Features.Get<IServerAddressesFeature>();
        var address = addressFeature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("FakeOtlpReceiver could not determine its bound address.");
        Port = new Uri(address).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

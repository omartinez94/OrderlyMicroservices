using System.Net;
using BuildingBlocks.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability.Tests.Integration;

/// <summary>
/// Phase 4 integration test: confirms that the
/// <see cref="ServiceCollectionExtensions.AddOrderlyOpenTelemetry"/>
/// + <see cref="LoggingBuilderExtensions.AddOrderlyOpenTelemetry"/>
/// extension methods produce a host whose trace pipeline exports a
/// real span to a fake OTLP HTTP receiver.
/// </summary>
/// <remarks>
/// The test boots a minimal ASP.NET Core <see cref="WebApplication"/>
/// that exposes a single <c>/live</c> endpoint. The OpenTelemetry
/// pipeline (ASP.NET Core instrumentation + BatchExportProcessor +
/// OTLP HTTP exporter) captures the request lifecycle as a span and
/// ships it to the fake receiver via the configured
/// <c>OpenTelemetry:Endpoint</c>. We assert that within a 10-second
/// deadline the receiver's <c>Traces</c> queue is non-empty.
/// </remarks>
public sealed class OrderlyOpenTelemetryTests
{
    [Fact]
    public async Task AddOrderlyOpenTelemetry_ExportsTraceToFakeOtlpReceiver()
    {
        // The OTel SDK's BatchExportProcessor has a 5s default schedule.
        // Shorten it so the test doesn't wait. OTEL_BSP_SCHEDULE_DELAY
        // is read at processor construction time, so it must be set
        // before the OpenTelemetryBuilder is built.
        Environment.SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY", "200");
        await using var receiver = new FakeOtlpReceiver();

        // The HTTP/protobuf OTLP exporter appends the signal-specific
        // path (/v1/traces, /v1/metrics, /v1/logs) to the configured
        // endpoint only when the endpoint is left at its default
        // (http://localhost:4318). When the endpoint is set explicitly
        // the path is preserved verbatim, so we pass the full
        // /v1/traces path so the exporter posts to the receiver.
        var traceEndpoint = $"http://127.0.0.1:{receiver.Port}/v1/traces";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
        });
        builder.Logging.AddOrderlyOpenTelemetry(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Enabled"] = "true",
            ["OpenTelemetry:Endpoint"] = traceEndpoint,
            ["OpenTelemetry:ServiceName"] = "orderly.fake",
            ["OpenTelemetry:ServiceVersion"] = "1.0.0",
            ["OpenTelemetry:LogsEnabled"] = "true",
            ["OpenTelemetry:OtlpProtocol"] = "HttpProtobuf"
        });
        builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.Fake");
        var app = builder.Build();
        app.MapGet("/live", () => Results.Ok());
        await app.StartAsync();

        try
        {
            // Resolve the app's bound port and fire a request. The
            // ASP.NET Core instrumentation captures the request as
            // a server span; the OTLP exporter ships it to the
            // fake receiver's /v1/traces endpoint.
            var server = app.Services.GetRequiredService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();
            var appAddress = addressFeature!.Addresses.First();
            var appPort = new Uri(appAddress).Port;

            using var http = new HttpClient();
            using var response = await http.GetAsync($"http://127.0.0.1:{appPort}/live");
            response.EnsureSuccessStatusCode();

            // Poll the receiver up to 8s. The export schedule is
            // 200ms so the first batch should land well within the
            // deadline. We do not call ForceFlush because the SDK
            // does not expose its TracerProvider / MeterProvider /
            // LoggerProvider as DI services — they're held by the
            // OpenTelemetrySdk static and are intentionally
            // not queryable from the IServiceProvider.
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && receiver.Traces.IsEmpty)
            {
                await Task.Delay(200);
            }

            receiver.Traces.Should().NotBeEmpty(
                "the OTLP trace exporter should push at least one span to /v1/traces within 8s of the /live request.");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Environment.SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY", null);
        }
    }
}

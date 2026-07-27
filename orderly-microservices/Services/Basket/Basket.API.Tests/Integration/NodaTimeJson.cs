using System.Text.Json;
using NodaTime.Serialization.SystemTextJson;

namespace Basket.API.Tests.Integration;

/// <summary>
/// Shared JSON helpers for the Basket integration tests. The
/// <see cref="System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync"/>
/// / <c>PostAsJsonAsync</c> helpers use the <c>HttpClient</c>'s
/// <em>default</em> <see cref="JsonSerializerOptions"/> — which DO
/// NOT have the NodaTime converter registered. The host's ASP.NET
/// Core pipeline uses the NodaTime-configured options
/// (<c>ConfigureHttpJsonOptions</c> in <c>Program.cs</c>), so a
/// <c>Basket</c> body sent through <c>PutAsJsonAsync</c> serializes
/// every <see cref="NodaTime.Instant"/> property as the empty
/// object <c>{}</c> (System.Text.Json's default for an
/// unrecognised type), and the host's deserializer rejects
/// <c>{}</c> as an invalid <c>Instant</c> with an empty-body
/// 400. <c>JsonContent.Create(value, options: ...)</c> with
/// <see cref="NodaTimeJsonOptions"/> produces the correct ISO-8601
/// string form that the host expects.
/// </summary>
internal static class NodaTimeJson
{
    /// <summary>
    /// Build a <see cref="HttpContent"/> for a <typeparamref name="T"/>
    /// using the host's NodaTime-aware JSON options. Drop-in
    /// replacement for <c>JsonContent.Create(value)</c> in the
    /// Basket integration tests.
    /// </summary>
    public static HttpContent CreateContent<T>(T value)
    {
        var options = NodaTimeJsonOptions;
        return JsonContent.Create(value, options: options);
    }

    /// <summary>
    /// Cached <see cref="JsonSerializerOptions"/> matching the
    /// host's <c>ConfigureHttpJsonOptions</c> configuration. The
    /// <c>ConfigureForNodaTime</c> call installs the
    /// <c>Instant</c> / <c>LocalDateTime</c> / <c>LocalDate</c> /
    /// <c>LocalTime</c> / <c>OffsetDateTime</c> / <c>ZonedDateTime</c>
    /// converters that write ISO-8601 strings (the host's
    /// expected wire format).
    /// </summary>
    public static JsonSerializerOptions NodaTimeJsonOptions { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
        };
        options.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
        return options;
    }
}

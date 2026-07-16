using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ordering.Infrastructure.Serialization;

/// <summary>
/// Cached <see cref="JsonSerializerOptions"/> shared by every
/// <c>OrderActivity</c> read/write. One static field allocated per
/// process — see <c>Basket §0.3.3</c> for the latent-bug fix the cached
/// options prevent.
/// </summary>
internal static class OrderActivityJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            // Enum values as strings ("Confirmed", not 2) — required by
            // OrderActivityMetadata's nullable status enums.
            new JsonStringEnumConverter(),
        },
    };
}
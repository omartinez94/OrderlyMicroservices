using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime.Text;

namespace Ordering.API.Serialization;

/// <summary>
/// Serializes <see cref="Instant"/> as an ISO-8601 string and accepts both
/// ISO-8601 strings and empty JSON objects (<c>{}</c>, produced by test
/// callers that do not register NodaTime converters on their client
/// <see cref="JsonSerializerOptions"/>) when reading.
/// </summary>
public sealed class LenientInstantJsonConverter : JsonConverter<Instant>
{
    public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return default;
            }

            ParseResult<Instant> parsed = InstantPattern.ExtendedIso.Parse(raw);
            if (!parsed.Success)
            {
                throw new JsonException($"Invalid Instant format: '{raw}'.");
            }

            return parsed.Value;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            reader.Skip();
            return default;
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing Instant.");
    }

    public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(InstantPattern.ExtendedIso.Format(value));
    }
}

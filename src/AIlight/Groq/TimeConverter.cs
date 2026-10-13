using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIlight.Groq;

/// <summary>
/// Zeitstempel tolerant lesen. Ältere Verlaufsdateien speicherten die Zeit als
/// Zahl (Sekunden seit 1970), neuere als ISO-Text — beides wird verstanden,
/// damit nach einem Umstieg nichts verloren geht.
/// </summary>
public sealed class FlexTimeConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString() ?? "";
                return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsed)
                    ? parsed
                    : DateTimeOffset.Now;

            case JsonTokenType.Number:
                var value = reader.GetDouble();
                // Große Zahlen sind Millisekunden, kleine sind Sekunden.
                return value > 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)value)
                    : DateTimeOffset.FromUnixTimeSeconds((long)value);

            default:
                return DateTimeOffset.Now;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}

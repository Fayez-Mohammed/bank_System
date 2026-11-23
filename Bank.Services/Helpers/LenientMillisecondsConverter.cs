using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.Services.Helpers;

/// <summary>
/// Reads a millisecond value that an LLM may send in a loose form: a whole number (12345),
/// a decimal (12345.0, 12345.6), exponent form (1.2345E4) or a string ("12345").
/// The value is rounded to the nearest whole millisecond. Anything that is not a usable
/// millisecond value (null, empty, text, negative, NaN, larger than int) becomes null
/// instead of failing the whole response.
/// </summary>
public sealed class LenientMillisecondsConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var whole))
                    return whole >= 0 ? whole : null;
                return reader.TryGetDouble(out var number) ? FromDouble(number) : null;

            case JsonTokenType.String:
                var text = reader.GetString();
                return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? FromDouble(parsed)
                    : null;

            default:
                // true/false, objects or arrays: not a timestamp. Skip the whole value.
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }

    private static int? FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;

        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded is >= 0 and <= int.MaxValue ? (int)rounded : null;
    }
}

using System.Text;
using System.Text.Json;

namespace ScreenLingo.Core;

// Display-only prefixes. Final translations always go through strict parsing.
internal static class JsonPreview
{
    internal record Field(string Name, string Value, bool Complete, int Depth);
    static byte[] Bytes(string raw)
    {
        raw = raw.TrimStart();
        if (raw.StartsWith("```")) { int newline = raw.IndexOf('\n'); raw = newline < 0 ? "" : raw[(newline + 1)..]; }
        return Encoding.UTF8.GetBytes(raw);
    }
    public static List<Field> Fields(string raw)
    {
        var fields = new List<Field>(); var bytes = Bytes(raw);
        var reader = new Utf8JsonReader(bytes, false, default); string? name = null;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) name = reader.GetString();
                else
                {
                    if (name is not null && reader.TokenType == JsonTokenType.String) fields.Add(new(name, reader.GetString()!, true, reader.CurrentDepth));
                    name = null;
                }
            }
            if (name is not null)
            {
                var tail = Encoding.UTF8.GetString(bytes.AsSpan((int)reader.BytesConsumed)).TrimStart();
                if (tail.StartsWith('"'))
                {
                    int safe = 1;
                    for (int i = 1; i < tail.Length; i++)
                    {
                        if (tail[i] == '\\')
                        {
                            int escape = i; if (++i >= tail.Length) break;
                            if (tail[i] == 'u') { if (i + 4 >= tail.Length) { i = escape; break; } i += 4; }
                        }
                        safe = i + 1;
                    }
                    string decoded = JsonSerializer.Deserialize<string>(tail[..safe] + "\"")!;
                    if (decoded.Length > 0 && char.IsHighSurrogate(decoded[^1])) decoded = decoded[..^1];
                    fields.Add(new(name, decoded, false, reader.CurrentDepth));
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { }
        return fields;
    }
    public static BilingualAlignment? Alignment(string raw, string english, string chinese)
    {
        var bytes = Bytes(raw); var reader = new Utf8JsonReader(bytes, false, default);
        int arrayStart = -1, lastPairEnd = -1; bool pairs = false;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1) pairs = reader.ValueTextEquals("pairs");
                if (pairs && reader.TokenType == JsonTokenType.StartArray && reader.CurrentDepth == 1) arrayStart = (int)reader.TokenStartIndex;
                if (arrayStart >= 0 && reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 2) lastPairEnd = (int)reader.BytesConsumed;
                if (arrayStart >= 0 && reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 1) break;
            }
            if (lastPairEnd < 0) return null;
            // Include only fully closed pairs; an unfinished occurrence number
            // must never temporarily point to the wrong repeated phrase.
            using var json = JsonDocument.Parse("{\"pairs\":" + Encoding.UTF8.GetString(bytes[arrayStart..lastPairEnd]) + "]}");
            return BilingualAlignment.Parse(json.RootElement, english, chinese);
        }
        catch (JsonException) { return null; }
    }
}

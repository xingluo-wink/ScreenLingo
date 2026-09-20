using System.Text.Json;

namespace ScreenLingo.Core;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;
}
public record AlignedPhrase(TextSpan English, TextSpan Chinese);
public record BilingualText(string English, string Chinese);
public sealed class OutputTruncatedException(string? partialText = null, bool reasoningOnly = false)
    : IOException(reasoningOnly ? "模型的思考过程耗尽了输出上限，尚未生成结果。请开启快速模式，或在额外参数中关闭该模型的思考。" : "输出被截断，请提高输出 Token 上限。")
{
    public string? PartialText { get; } = partialText;
    public bool ReasoningOnly { get; } = reasoningOnly;
}

public sealed class BilingualAlignment(string english, string chinese, IReadOnlyList<AlignedPhrase> phrases, bool isPartial = false)
{
    public string English { get; } = english;
    public string Chinese { get; } = chinese;
    public IReadOnlyList<AlignedPhrase> Phrases { get; } = phrases;
    public bool IsPartial { get; } = isPartial;

    public IReadOnlyList<TextSpan> Match(string language, int start, int length)
    {
        if (length <= 0 || start < 0 || language is not ("en" or "zh")) return [];
        var selection = new TextSpan(start, length);
        var hits = Phrases.Where(p => (language == "en" ? p.English : p.Chinese).Overlaps(selection))
            .Select(p => language == "en" ? p.Chinese : p.English).Distinct().OrderBy(s => s.Start);
        var merged = new List<TextSpan>();
        foreach (var hit in hits)
        {
            if (merged.Count > 0 && merged[^1].End >= hit.Start)
                merged[^1] = new(merged[^1].Start, Math.Max(merged[^1].End, hit.End) - merged[^1].Start);
            else merged.Add(hit);
        }
        return merged;
    }

    public static BilingualAlignment Parse(JsonElement root, string english, string chinese)
    {
        var pairs = new List<AlignedPhrase>();
        if (root.ValueKind != JsonValueKind.Object ||
            (!root.TryGetProperty("pairs", out var items) && !root.TryGetProperty("alignment", out items)) || items.ValueKind != JsonValueKind.Array)
            return new(english, chinese, pairs);
        foreach (var item in items.EnumerateArray().Take(4000))
        {
            if (item.ValueKind != JsonValueKind.Object && !(item.ValueKind == JsonValueKind.Array && item.GetArrayLength() is 2 or 4)) continue;
            var en = Locate(item, "en", english); var zh = Locate(item, "zh", chinese);
            if (en is not null && zh is not null) pairs.Add(new(en.Value, zh.Value));
        }
        return new(english, chinese, pairs.Distinct().ToArray());
    }

    static TextSpan? Locate(JsonElement item, string language, string text)
    {
        JsonElement value, ordinal = default;
        bool hasOrdinal;
        if (item.ValueKind == JsonValueKind.Array)
        {
            int index = language == "en" ? 0 : 1; value = item[index];
            hasOrdinal = item.GetArrayLength() == 4;
            if (hasOrdinal) ordinal = item[index + 2];
        }
        else
        {
            if (!item.TryGetProperty(language, out value)) return null;
            hasOrdinal = item.TryGetProperty(language + "Occurrence", out ordinal);
        }
        if (value.ValueKind != JsonValueKind.String) return null;
        string phrase = value.GetString()!;
        if (string.IsNullOrWhiteSpace(phrase)) return null;
        int occurrence = 0;
        if (hasOrdinal &&
            (ordinal.ValueKind != JsonValueKind.Number || !ordinal.TryGetInt32(out occurrence) || occurrence < 1)) return null;
        var matches = new List<int>();
        for (int offset = 0; offset <= text.Length - phrase.Length;)
        {
            int found = text.IndexOf(phrase, offset, StringComparison.Ordinal);
            if (found < 0) break;
            int end = found + phrase.Length;
            bool insideWord = language == "en" &&
                ((char.IsLetterOrDigit(phrase[0]) && found > 0 && char.IsLetterOrDigit(text[found - 1])) ||
                 (char.IsLetterOrDigit(phrase[^1]) && end < text.Length && char.IsLetterOrDigit(text[end])));
            if (!insideWord) matches.Add(found);
            offset = end;
        }
        // Ambiguous repetitions without an occurrence number are not guessed.
        if (occurrence == 0) return matches.Count == 1 ? new(matches[0], phrase.Length) : null;
        return occurrence <= matches.Count ? new(matches[occurrence - 1], phrase.Length) : null;
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenLingo.Core;

public record OcrLine(string Text, double X, double Y, double Width, double Height, double Confidence);
public record TextRegion(string Id, string Text, double X, double Y, double Width, double Height, double FontHeight, double Confidence);
public record OcrRequest(byte[] Png, int MaxSide = 1920);
public record OcrResponse(List<OcrLine> Lines, double Milliseconds, string? Error = null);

public enum ApiProtocol { ChatCompletions, Responses, Anthropic, Gemini, QwenMt }

public sealed class ApiProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的 API";
    public ApiProtocol Protocol { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ProtectedKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 4096;
    public string ExtraBody { get; set; } = "{}";
    public string Style { get; set; } = "忠实、自然，保留术语、数字、代码和网址，不添加解释。";
    public override string ToString() => Name;
    public ApiProfile Clone() => JsonSerializer.Deserialize<ApiProfile>(JsonSerializer.Serialize(this))!;
}

public sealed class AppSettings
{
    public List<ApiProfile> Profiles { get; set; } = [new()];
    public string ActiveProfileId { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Alt+Q";
    public int IdleSeconds { get; set; } = 120;
    public string LastTarget { get; set; } = "zh";
    public string ScreenshotDirectory { get; set; } = "";
    public ApiProfile? ActiveProfile => Profiles.FirstOrDefault(x => x.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();
}

public static class LayoutGrouper
{
    // Conservative vertical grouping: never concatenate neighbours on the same row.
    // Short menu labels remain separate; wrapped prose can form a semantic paragraph.
    public static List<TextRegion> Group(IEnumerable<OcrLine> source)
    {
        var lines = source.Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Width > 0 && l.Height > 0)
            .OrderBy(l => l.Y).ThenBy(l => l.X).ToList();
        var groups = new List<List<OcrLine>>();
        foreach (var line in lines)
        {
            var best = groups.Select(g => (g, last: g[^1]))
                .Where(p => CanJoin(p.last, line))
                .OrderBy(p => line.Y - p.last.Y).FirstOrDefault();
            if (best.g is null) groups.Add([line]); else best.g.Add(line);
        }
        return groups.OrderBy(g => g[0].Y).ThenBy(g => g[0].X).Select((g, i) =>
        {
            var x = g.Min(l => l.X); var y = g.Min(l => l.Y);
            var text = g[0].Text.Trim();
            foreach (var l in g.Skip(1))
            {
                var next = l.Text.Trim();
                if (text.EndsWith('-') && text.Length > 1 && char.IsLetter(text[^2]) && char.IsLower(next[0]))
                    text = text[..^1] + next;
                else text += IsCjk(text[^1]) && IsCjk(next[0]) ? next : " " + next;
            }
            return new TextRegion($"b{i + 1}", text, x, y, g.Max(l => l.X + l.Width) - x,
                g.Max(l => l.Y + l.Height) - y, g.Average(l => l.Height), g.Min(l => l.Confidence));
        }).ToList();
    }

    static bool CanJoin(OcrLine a, OcrLine b)
    {
        var h = Math.Max(a.Height, b.Height);
        var gap = b.Y - (a.Y + a.Height);
        if (gap < -h * .15 || gap > h * .85 || b.Y < a.Y + a.Height * .7) return false;
        if (Math.Min(a.Height, b.Height) / h < .88 || Math.Abs(a.X - b.X) > h * .85) return false;
        if (a.Width < h * 8 || a.Text.Length < 18) return false;
        if (Regex.IsMatch(a.Text.TrimEnd(), @"[。！？.!?:：;；]$") || Regex.IsMatch(b.Text, @"^\s*(?:[-•●]|\d+[.)、])")) return false;
        return true;
    }
    static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9fff';
}

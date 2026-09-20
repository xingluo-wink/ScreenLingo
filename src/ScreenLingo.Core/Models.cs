using System.Text.Json;

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
    public bool StreamResponses { get; set; } = true;
    public bool FastMode { get; set; } = true;
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
    public int ReadingFontSize { get; set; } = 16;
    public bool BilingualDisplay { get; set; }
    public bool BilingualSideBySide { get; set; }
    public bool ReaderTopmost { get; set; } = true;
    public double ReaderWidth { get; set; }
    public double ReaderHeight { get; set; }
    public string ScreenshotDirectory { get; set; } = "";
    public ApiProfile? ActiveProfile => Profiles.FirstOrDefault(x => x.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();
}

public static class LayoutGrouper
{
    // The user's selection is the translation unit. OCR boxes only determine
    // reading order and the overall bounds; they never create separate blocks.
    public static List<TextRegion> Group(IEnumerable<OcrLine> source)
    {
        var lines = source.Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Width > 0 && l.Height > 0)
            .OrderBy(l => l.Y).ThenBy(l => l.X).ToList();
        if (lines.Count == 0) return [];
        var x = lines.Min(l => l.X); var y = lines.Min(l => l.Y);
        var text = lines[0].Text.Trim();
        foreach (var l in lines.Skip(1))
        {
            var next = l.Text.Trim();
            if (text.EndsWith('-') && text.Length > 1 && char.IsLetter(text[^2]) && char.IsLower(next[0]))
                text = text[..^1] + next;
            else text += IsCjk(text[^1]) && IsCjk(next[0]) ? next : " " + next;
        }
        return [new TextRegion("b1", text, x, y, lines.Max(l => l.X + l.Width) - x,
            lines.Max(l => l.Y + l.Height) - y, lines.Average(l => l.Height), lines.Min(l => l.Confidence))];
    }
    static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9fff' or >= '\u3000' and <= '\u303f' or >= '\uff00' and <= '\uffef';
}

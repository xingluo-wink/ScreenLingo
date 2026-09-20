using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ScreenLingo.Core;

// SSE is consumed off the UI thread. Only text deltas are exposed; thinking,
// tool calls and provider error bodies never become reader content.
internal static class StreamingResponse
{
    public static async Task<string> ReadAsync(Stream stream, ApiProtocol protocol, Action<string>? preview, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder(); var data = new StringBuilder();
        var clock = Stopwatch.StartNew(); long last = -125; int received = 0; bool completed = false, sawReasoning = false;
        void Publish(bool force = false)
        {
            if (text.Length == 0 || preview is null || (!force && clock.ElapsedMilliseconds - last < 125)) return;
            last = clock.ElapsedMilliseconds; preview(text.ToString());
        }
        bool Event()
        {
            if (data.Length == 0) return false;
            string value = data.ToString().Trim(); data.Clear();
            if (value == "[DONE]") return true;
            using var json = JsonDocument.Parse(value); var root = json.RootElement;
            string? type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is "error" or "response.failed" || root.TryGetProperty("error", out _))
                throw new IOException("接口在生成过程中返回错误，请重试。");
            if (protocol == ApiProtocol.Responses)
            {
                if (type == "response.output_text.delta") text.Append(root.GetProperty("delta").GetString());
                if (type == "response.incomplete") throw new OutputTruncatedException(text.ToString());
                if (type == "response.completed")
                {
                    completed = true;
                    if (root.TryGetProperty("response", out var response) && (response.TryGetProperty("output", out _) || response.TryGetProperty("output_text", out _)))
                    { text.Clear(); text.Append(TranslationService.ExtractText(response, protocol)); }
                }
            }
            else if (protocol == ApiProtocol.Anthropic)
            {
                if (type == "content_block_start" && root.TryGetProperty("content_block", out var block) && block.GetProperty("type").GetString() == "text")
                    text.Append(block.GetProperty("text").GetString());
                if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta) && delta.GetProperty("type").GetString() == "text_delta")
                    text.Append(delta.GetProperty("text").GetString());
                if (type == "message_delta" && root.GetProperty("delta").TryGetProperty("stop_reason", out var stop) && stop.GetString() == "max_tokens")
                    throw new OutputTruncatedException(text.ToString());
                if (type == "message_stop") completed = true;
            }
            else if (protocol == ApiProtocol.Gemini)
            {
                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                        foreach (var part in parts.EnumerateArray())
                            if (part.TryGetProperty("text", out var partText) && !(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)) text.Append(partText.GetString());
                    if (candidate.TryGetProperty("finishReason", out var reason))
                    {
                        if (reason.GetString() == "MAX_TOKENS") throw new OutputTruncatedException(text.ToString());
                        if (reason.GetString() != "STOP") throw new IOException("模型停止了生成，未返回完整译文。");
                        completed = true;
                    }
                }
            }
            else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var reasoningDelta) && reasoningDelta.TryGetProperty("reasoning_content", out var thought) && thought.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(thought.GetString())) sawReasoning = true;
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) text.Append(content.GetString());
                if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
                {
                    if (finish.GetString() == "length") throw new OutputTruncatedException(text.ToString(), sawReasoning && string.IsNullOrWhiteSpace(text.ToString()));
                    if (finish.GetString() != "stop") throw new IOException("模型停止了生成，未返回完整译文。");
                    completed = true;
                }
            }
            Publish(); return false;
        }
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            received += line.Length + 1;
            if (received > 4 * 1024 * 1024) throw new InvalidDataException("API 响应过大。");
            if (line.Length == 0) { if (Event()) break; }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
        if (data.Length > 0) Event();
        ct.ThrowIfCancellationRequested();
        if (!completed) throw new IOException("连接在译文完成前中断，请重试；未完成内容不会存入缓存。");
        Publish(true); return text.ToString();
    }
}

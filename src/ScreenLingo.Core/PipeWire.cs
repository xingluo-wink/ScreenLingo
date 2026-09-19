using System.Buffers.Binary;
using System.Text.Json;

namespace ScreenLingo.Core;

public static class PipeWire
{
    const int MaxMessage = 48 * 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > MaxMessage) throw new IOException("选区过大，请缩小框选范围。");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(data, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaxMessage) throw new IOException("无效的 OCR 消息大小。");
        var data = new byte[length]; await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data) ?? throw new IOException("OCR 返回了空结果。");
    }
}

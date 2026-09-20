using System.Windows.Documents;
using ScreenLingo.Core;

namespace ScreenLingo;

// Paint above the document without modifying its Runs, selection or clipboard
// text. Native selection remains free to move while the counterpart is marked.
sealed class LinkedTextAdorner(ReadingContent reader,Func<IReadOnlyList<(Run Text,TextSpan Span)>> ranges):Adorner(reader)
{
    static readonly Brush marker = CreateMarker();
    IReadOnlyList<Rect>? cached;
    public void InvalidateGeometry() { cached = null; InvalidateVisual(); }
    public IReadOnlyList<Rect> GetRectangles()
    {
        if (cached is not null) return cached;
        var current = ranges();
        if (current.Any(r => !r.Text.ContentStart.HasValidLayout)) return [];
        return cached = Rectangles(current);
    }
    static Brush CreateMarker()
    {
        var brush = new SolidColorBrush(Color.FromArgb(78, 26, 170, 156));
        brush.Freeze(); return brush;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, reader.ActualWidth, reader.ActualHeight)));
        foreach (var rect in GetRectangles()) dc.DrawRoundedRectangle(marker, null, rect, 2, 2);
        dc.Pop();
    }

    public static IReadOnlyList<Rect> Rectangles(IReadOnlyList<(Run Text,TextSpan Span)> ranges)
    {
        var result = new List<Rect>();
        foreach (var (text, span) in ranges)
        {
            if (!text.ContentStart.HasValidLayout) continue;
            var cursor = text.ContentStart.GetPositionAtOffset(span.Start, LogicalDirection.Forward);
            var end = text.ContentStart.GetPositionAtOffset(Math.Min(span.End, text.Text.Length), LogicalDirection.Backward);
            if (cursor is null || end is null) continue;
            // Ask WPF once per visual line instead of measuring every character.
            // Backward gravity keeps the right edge on the preceding wrapped line.
            while (cursor.CompareTo(end) < 0)
            {
                var next = cursor.GetLineStartPosition(1, out int moved);
                var lineEnd = moved == 0 || next is null || next.CompareTo(end) >= 0 ? end : next;
                int endOffset = text.ContentStart.GetOffsetToPosition(lineEnd);
                int startOffset = text.ContentStart.GetOffsetToPosition(cursor);
                while (endOffset > startOffset && char.IsControl(text.Text[endOffset - 1])) endOffset--;
                var visibleEnd = text.ContentStart.GetPositionAtOffset(endOffset, LogicalDirection.Backward)!;
                var a = cursor.GetCharacterRect(LogicalDirection.Forward);
                var b = visibleEnd.GetCharacterRect(LogicalDirection.Backward);
                if (endOffset > startOffset && !a.IsEmpty && !b.IsEmpty && Math.Abs(a.Top - b.Top) < 1 && b.Right > a.Left)
                {
                    var rect = new Rect(a.Left, a.Top, b.Right - a.Left, Math.Max(a.Height, b.Height));
                    if (result.Count > 0 && Math.Abs(result[^1].Top - rect.Top) < 1 && rect.Left <= result[^1].Right + 1 && rect.Right >= result[^1].Left)
                    {
                        var previous = result[^1]; previous.Union(rect); result[^1] = previous;
                    }
                    else result.Add(rect);
                }
                if (lineEnd.CompareTo(end) >= 0 || lineEnd.CompareTo(cursor) <= 0) break;
                cursor = lineEnd.GetPositionAtOffset(0, LogicalDirection.Forward)!;
            }
        }
        return result;
    }
}

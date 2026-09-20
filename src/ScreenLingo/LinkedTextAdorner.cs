using System.Windows.Documents;
using ScreenLingo.Core;

namespace ScreenLingo;

// Paint above the document without modifying its Runs, selection or clipboard
// text. Native selection remains free to move while the counterpart is marked.
sealed class LinkedTextAdorner(ReadingContent reader,Func<IReadOnlyList<(Run Text,TextSpan Span)>> ranges):Adorner(reader)
{
    static readonly Brush marker = CreateMarker();
    static Brush CreateMarker()
    {
        var brush = new SolidColorBrush(Color.FromArgb(78, 26, 170, 156));
        brush.Freeze(); return brush;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, reader.ActualWidth, reader.ActualHeight)));
        foreach (var rect in Rectangles(ranges())) dc.DrawRoundedRectangle(marker, null, rect, 2, 2);
        dc.Pop();
    }

    public static IReadOnlyList<Rect> Rectangles(IReadOnlyList<(Run Text,TextSpan Span)> ranges)
    {
        var result = new List<Rect>();
        foreach (var (text, span) in ranges)
        {
            if (!text.ContentStart.HasValidLayout) continue;
            for (int i = span.Start; i < span.End; i++)
            {
                if (i >= text.Text.Length || char.IsControl(text.Text[i])) continue;
                var start = text.ContentStart.GetPositionAtOffset(i, LogicalDirection.Forward);
                var end = text.ContentStart.GetPositionAtOffset(i + 1, LogicalDirection.Backward);
                if (start is null || end is null) continue;
                var a = start.GetCharacterRect(LogicalDirection.Forward);
                var b = end.GetCharacterRect(LogicalDirection.Backward);
                if (a.IsEmpty || b.IsEmpty || Math.Abs(a.Top - b.Top) > 1 || b.Right <= a.Left) continue;
                var rect = new Rect(a.Left, a.Top, b.Right - a.Left, Math.Max(a.Height, b.Height));
                if (result.Count > 0 && Math.Abs(result[^1].Top - rect.Top) < 1 && rect.Left <= result[^1].Right + 1 && rect.Right >= result[^1].Left)
                {
                    var previous = result[^1]; previous.Union(rect); result[^1] = previous;
                }
                else result.Add(rect);
            }
        }
        return result;
    }
}

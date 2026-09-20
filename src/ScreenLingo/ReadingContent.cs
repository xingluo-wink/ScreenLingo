using System.Windows.Documents;
using ScreenLingo.Core;
namespace ScreenLingo;

public enum ReadingMode { Chinese,English,Bilingual }

// A single selectable document owns wrapping and scrolling. Progress changes
// individual runs rather than replacing the page and losing the selection.
public sealed class ReadingContent:RichTextBox
{
 public static readonly int[] FontSizes=[12,14,16,18,20,24,28,32,36,40];
 readonly List<(string Id,string Language,Run Text)> runs=[];
 IReadOnlyList<TextRegion>? source;
 ReadingMode mode;bool columns,sourceOnly;
 bool presenting;BilingualAlignment? alignment;LinkedTextAdorner? highlighter;AdornerLayer? adorners;
 readonly List<(Run Text,TextSpan Span)> linkedRanges=[];
 public string LinkedSelectionLabel { get; private set; }="";
 public event EventHandler? LinkedSelectionChanged;
 public IReadOnlyList<Rect> LinkedHighlightRects=>LinkedTextAdorner.Rectangles(linkedRanges);
 public bool UsesColumns=>columns;
 public string PlainText=>new TextRange(Document.ContentStart,Document.ContentEnd).Text.Trim();
 public ReadingContent()
 {
  IsReadOnly=true;IsUndoEnabled=false;IsReadOnlyCaretVisible=false;AcceptsTab=false;
  IsInactiveSelectionHighlightEnabled=true;
  BorderThickness=new Thickness(0);Padding=new Thickness(22,18,22,22);Background=Brushes.White;
  VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
  UseLayoutRounding=true;SnapsToDevicePixels=true;MinHeight=0;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);Document=NewDocument(16);
  SelectionChanged+=(_,_)=>UpdateLinkedSelection();
  AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,_)=>highlighter?.InvalidateVisual()));
  SizeChanged+=(_,_)=>highlighter?.InvalidateVisual();
  Loaded+=(_,_)=>
  {
   adorners=AdornerLayer.GetAdornerLayer(this);
   if(adorners is not null&&highlighter is null){highlighter=new(this,()=>linkedRanges){IsHitTestVisible=false};adorners.Add(highlighter);}
   UpdateLinkedSelection();
  };
  Unloaded+=(_,_)=>{if(highlighter is not null)adorners?.Remove(highlighter);highlighter=null;adorners=null;};
 }
 public void SetAlignment(BilingualAlignment? value)
 {
  if(ReferenceEquals(alignment,value))return;alignment=value;UpdateLinkedSelection();
 }
 void UpdateLinkedSelection()
 {
  if(presenting)return;linkedRanges.Clear();string label="";
  if(mode==ReadingMode.Bilingual&&!sourceOnly&&alignment is not null&&!Selection.IsEmpty)
  {
   var selected=runs.Where(r=>(r.Language is "en" or "zh")&&Selection.Start.CompareTo(r.Text.ContentEnd)<0&&Selection.End.CompareTo(r.Text.ContentStart)>0).ToList();
   if(selected.Count==1)
   {
    var from=selected[0];var to=runs.FirstOrDefault(r=>r.Id==from.Id&&r.Language==(from.Language=="en"?"zh":"en"));
    if(to.Text is not null&&from.Text.Text==(from.Language=="en"?alignment.English:alignment.Chinese)&&to.Text.Text==(from.Language=="en"?alignment.Chinese:alignment.English))
    {
     var start=Selection.Start.CompareTo(from.Text.ContentStart)<0?from.Text.ContentStart:Selection.Start;
     var end=Selection.End.CompareTo(from.Text.ContentEnd)>0?from.Text.ContentEnd:Selection.End;
     int offset=from.Text.ContentStart.GetOffsetToPosition(start);
     int length=start.GetOffsetToPosition(end);
     if(!string.IsNullOrWhiteSpace(new TextRange(start,end).Text))
     {
      foreach(var span in alignment.Match(from.Language,offset,length))
       if(span.Start>=0&&span.End<=to.Text.Text.Length)linkedRanges.Add((to.Text,span));
      if(linkedRanges.Count>0)label=(to.Language=="en"?"对应英文：":"对应中文：")+string.Join(" … ",linkedRanges.Select(r=>r.Text.Text.Substring(r.Span.Start,r.Span.Length)));
     }
    }
   }
  }
  highlighter?.InvalidateVisual();
  if(LinkedSelectionLabel!=label){LinkedSelectionLabel=label;LinkedSelectionChanged?.Invoke(this,EventArgs.Empty);}
 }
 static FlowDocument NewDocument(int size)=>new(){PagePadding=new Thickness(0),ColumnWidth=double.PositiveInfinity,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=size,LineHeight=Math.Ceiling(size*1.6),Foreground=Ui.Ink,TextAlignment=TextAlignment.Left};
 public void Present(IReadOnlyList<TextRegion> regions,IReadOnlyDictionary<string,string> chinese,IReadOnlyDictionary<string,string> english,
  ReadingMode requestedMode,bool requestedColumns,int fontSize,bool showSource,bool busy=false)
 {
  presenting=true;
  double offset=VerticalOffset;TextPointer? caret=null;double oldY=double.NaN;
  if(IsArrangeValid&&ActualWidth>0&&ActualHeight>0){caret=GetPositionFromPoint(new Point(Padding.Left+2,Padding.Top+2),true);oldY=caret?.GetCharacterRect(LogicalDirection.Forward).Top??double.NaN;}
  bool rebuild=!ReferenceEquals(source,regions)||mode!=requestedMode||columns!=requestedColumns||sourceOnly!=showSource;
  if(rebuild)
  {
   source=regions;mode=requestedMode;columns=requestedColumns;sourceOnly=showSource;runs.Clear();Document=NewDocument(fontSize);
   if(mode==ReadingMode.Bilingual&&!showSource&&columns)
   {
    var table=new Table{CellSpacing=0};table.Columns.Add(new());table.Columns.Add(new());var rows=new TableRowGroup();table.RowGroups.Add(rows);Document.Blocks.Add(table);
    var header=new TableRow();header.Cells.Add(Header("English"));header.Cells.Add(Header("中文"));rows.Rows.Add(header);
    foreach(var region in regions){var row=new TableRow();row.Cells.Add(Cell(region,"en"));row.Cells.Add(Cell(region,"zh"));rows.Rows.Add(row);}
   }
   else
   {
    foreach(var region in regions)
    {
     if(mode==ReadingMode.Bilingual&&!showSource)
     {
      var section=new Section{Margin=new Thickness(0,0,0,20)};section.Blocks.Add(Paragraph(region,"en",new Thickness(0,0,0,6)));
      var translation=Paragraph(region,"zh",new Thickness(0));translation.Foreground=new SolidColorBrush(Color.FromRgb(69,90,94));section.Blocks.Add(translation);Document.Blocks.Add(section);
     }
     else Document.Blocks.Add(Paragraph(region,showSource?"source":mode==ReadingMode.English?"en":"zh",new Thickness(0,0,0,14)));
    }
   }
  }
  Document.FontSize=fontSize;Document.LineHeight=Math.Ceiling(fontSize*1.6);
  var originals=regions.ToDictionary(r=>r.Id,r=>r.Text);
  foreach(var entry in runs)
  {
   string? text=entry.Language=="source"?originals[entry.Id]:(entry.Language=="en"?english:chinese).GetValueOrDefault(entry.Id);
   var value=text??(busy?"正在翻译…":"尚无译文");if(entry.Text.Text!=value)entry.Text.Text=value;
   if(text is null)entry.Text.Foreground=Ui.Muted;else entry.Text.ClearValue(TextElement.ForegroundProperty);
  }
  UpdateLayout();
  if(!rebuild&&caret is not null&&!double.IsNaN(oldY))
  {
   var newY=caret.GetCharacterRect(LogicalDirection.Forward).Top;if(double.IsFinite(newY))offset+=newY-oldY;
  }
  ScrollToVerticalOffset(Math.Max(0,offset));
  presenting=false;UpdateLinkedSelection();
 }
 Paragraph Paragraph(TextRegion region,string language,Thickness margin)
 {
  var run=new Run();runs.Add((region.Id,language,run));return new Paragraph(run){Margin=margin};
 }
 TableCell Cell(TextRegion region,string language)=>new(Paragraph(region,language,new Thickness(0))){Padding=new Thickness(language=="en"?0:14,10,language=="en"?14:0,14),BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1)};
 static TableCell Header(string label)=>new(new Paragraph(new Run(label)){FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Ui.Accent,Margin=new Thickness(0)}){Padding=new Thickness(label=="中文"?14:0,0,0,6)};
 public static string BilingualText(IEnumerable<TextRegion> regions,IReadOnlyDictionary<string,string> chinese,IReadOnlyDictionary<string,string> english)
  =>string.Join("\n\n",regions.Select(r=>$"English: {english.GetValueOrDefault(r.Id)??"尚无译文"}\n中文：{chinese.GetValueOrDefault(r.Id)??"尚无译文"}"));
}

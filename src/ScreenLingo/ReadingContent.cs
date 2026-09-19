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
 public bool UsesColumns=>columns;
 public string PlainText=>new TextRange(Document.ContentStart,Document.ContentEnd).Text.Trim();
 public ReadingContent()
 {
  IsReadOnly=true;IsUndoEnabled=false;IsReadOnlyCaretVisible=false;AcceptsTab=false;
  BorderThickness=new Thickness(0);Padding=new Thickness(22,18,22,22);Background=Brushes.White;
  VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
  UseLayoutRounding=true;SnapsToDevicePixels=true;MinHeight=0;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);Document=NewDocument(16);
 }
 static FlowDocument NewDocument(int size)=>new(){PagePadding=new Thickness(0),ColumnWidth=double.PositiveInfinity,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=size,LineHeight=Math.Ceiling(size*1.6),Foreground=Ui.Ink,TextAlignment=TextAlignment.Left};
 public void Present(IReadOnlyList<TextRegion> regions,IReadOnlyDictionary<string,string> chinese,IReadOnlyDictionary<string,string> english,
  ReadingMode requestedMode,bool requestedColumns,int fontSize,bool showSource,bool busy=false)
 {
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

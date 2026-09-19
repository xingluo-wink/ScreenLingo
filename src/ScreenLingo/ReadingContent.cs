using ScreenLingo.Core;
namespace ScreenLingo;

// The on-screen reader and exported image share the same wrapping and font size.
public static class ReadingContent
{
 public static readonly int[] FontSizes=[12,14,16,18,20,24,28,32,36,40];

 public static Border Card(TextRegion region,IReadOnlyDictionary<string,string> chinese,
  IReadOnlyDictionary<string,string> english,string target,bool bilingual,int fontSize)
 {
  UIElement body;
  if(bilingual)
  {
   var pair=new Grid();pair.ColumnDefinitions.Add(new());pair.ColumnDefinitions.Add(new(){Width=new GridLength(25)});pair.ColumnDefinitions.Add(new());
   pair.Children.Add(Column("中文",chinese.GetValueOrDefault(region.Id),fontSize));
   var line=new Border{Width=1,Background=Ui.Line};Grid.SetColumn(line,1);pair.Children.Add(line);
   var right=Column("English",english.GetValueOrDefault(region.Id),fontSize);Grid.SetColumn(right,2);pair.Children.Add(right);body=pair;
  }
  else body=Body((target=="en"?english:chinese).GetValueOrDefault(region.Id)??region.Text,fontSize);
  var card=Ui.Card(body,new Thickness(12,10,12,10));card.Margin=new Thickness(0,0,0,8);return card;
 }
 static StackPanel Column(string label,string? text,int fontSize)
 {
  var panel=new StackPanel();var heading=Ui.Text(label,12,Ui.Accent);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,6);panel.Children.Add(heading);
  var content=Body(text??"尚未翻译",fontSize);if(text is null)content.Foreground=Ui.Muted;panel.Children.Add(content);return panel;
 }
 static TextBlock Body(string text,int fontSize)
 {
  var block=Ui.Text(text,fontSize);block.LineHeight=Math.Ceiling(fontSize*1.55);return block;
 }
 public static string BilingualText(IEnumerable<TextRegion> regions,IReadOnlyDictionary<string,string> chinese,IReadOnlyDictionary<string,string> english)
  =>string.Join("\n\n",regions.Select(r=>$"中文：{chinese.GetValueOrDefault(r.Id)??"尚未翻译"}\nEnglish: {english.GetValueOrDefault(r.Id)??"尚未翻译"}"));
}

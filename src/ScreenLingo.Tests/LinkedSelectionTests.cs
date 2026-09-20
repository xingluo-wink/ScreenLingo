using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLingo.Core;
namespace ScreenLingo.Tests;

static class LinkedSelectionTests
{
 public static async Task Run(Action<string,bool,object?> check,string work)
 {
  var reader=new ReadingContent();var window=new Window{Width=800,Height=400,Content=reader,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
  TextRegion[] regions=[new("b1",BilingualTests.English,0,0,800,80,24,1)];
  Dictionary<string,string> en=new(){{"b1",BilingualTests.English}},zh=new(){{"b1",BilingualTests.Chinese}};
  reader.Present(regions,zh,en,ReadingMode.Bilingual,false,24,false);reader.SetAlignment(BilingualTests.Sample());window.Show();
  try
  {
   await Task.Delay(40);string original=reader.PlainText;
   Select(reader,BilingualTests.Chinese,0,2);await Task.Delay(20);
   check("select Chinese hello highlights English hello",reader.LinkedSelectionLabel=="对应英文：hello"&&reader.LinkedHighlightRects.Count==1&&reader.LinkedHighlightRects[0].Width>10,reader.LinkedHighlightRects);
   check("paired highlight keeps the native selection and copied text",reader.Selection.Text=="你好"&&reader.PlainText==original,null);
   Save(window,Path.Combine(work,"reader-0.3.0-linked-hello.png"));
   Select(reader,BilingualTests.English,14,5);await Task.Delay(20);
   var expected=Find(reader,BilingualTests.Chinese).ContentStart.GetPositionAtOffset(8)!.GetCharacterRect(LogicalDirection.Forward);
   check("English selection marks only the second Chinese occurrence",reader.LinkedSelectionLabel=="对应中文：你好"&&reader.LinkedHighlightRects.Count==1&&Math.Abs(reader.LinkedHighlightRects[0].Left-expected.Left)<1,reader.LinkedHighlightRects);
   Select(reader,BilingualTests.English,20,5);await Task.Delay(20);
   check("reverse-order phrase selection highlights its counterpart",reader.LinkedSelectionLabel=="对应中文：再次",null);
   Select(reader,BilingualTests.Chinese,3,2);var document=reader.Document;
   reader.Present(regions,zh,en,ReadingMode.Bilingual,false,32,false);await Task.Delay(20);
   check("font changes preserve selection and refresh highlight geometry",ReferenceEquals(document,reader.Document)&&reader.Selection.Text=="世界"&&reader.LinkedSelectionLabel=="对应英文：world"&&reader.LinkedHighlightRects.Count>0,null);
   reader.SelectAll();check("cross-language selection remains ordinary copy selection",reader.LinkedHighlightRects.Count==0&&reader.Selection.Text.Contains(BilingualTests.English)&&reader.Selection.Text.Contains(BilingualTests.Chinese),null);
   reader.Selection.Select(reader.Document.ContentStart,reader.Document.ContentStart);check("clearing selection clears the counterpart highlight",reader.LinkedSelectionLabel.Length==0&&reader.LinkedHighlightRects.Count==0,null);
   reader.Present(regions,zh,en,ReadingMode.Bilingual,true,24,false);await Task.Delay(20);Select(reader,BilingualTests.Chinese,0,2);
   check("linked selection also works in two columns",reader.UsesColumns&&reader.LinkedSelectionLabel=="对应英文：hello"&&reader.LinkedHighlightRects.Count>0,null);
   Save(window,Path.Combine(work,"reader-0.3.0-linked-columns.png"));
   reader.Present(regions,zh,en,ReadingMode.English,false,24,false);check("single language mode clears linked highlighting",reader.LinkedHighlightRects.Count==0,null);

   en["b1"]="🙂\nhello";zh["b1"]="🙂\n你好";
   reader.Present(regions,zh,en,ReadingMode.Bilingual,false,24,false);reader.SetAlignment(new(en["b1"],zh["b1"],[new(new(3,5),new(3,2))]));await Task.Delay(20);
   Select(reader,zh["b1"],3,2);
   check("newlines and emoji preserve selection offsets in the WPF document",reader.LinkedSelectionLabel=="对应英文：hello"&&reader.LinkedHighlightRects.Count==1,null);

   string longEn=string.Join(" ",Enumerable.Repeat("A long matching phrase wraps naturally across the window.",12));
   string longZh="这是一段很长的对应内容。";
   en["b1"]=longEn;zh["b1"]=longZh;
   reader.Present(regions,zh,en,ReadingMode.Bilingual,false,24,false);reader.SetAlignment(new(longEn,longZh,[new(new(0,longEn.Length),new(0,longZh.Length))]));await Task.Delay(30);
   Select(reader,longZh,0,longZh.Length);reader.ScrollToHome();await Task.Delay(20);
   var before=reader.LinkedHighlightRects.ToArray();reader.ScrollToVerticalOffset(160);await Task.Delay(30);var after=reader.LinkedHighlightRects;
   check("wrapped counterpart highlights every rendered line",before.Length>=4&&before.All(r=>r.Width>0&&r.Width<=reader.ActualWidth),new{lines=before.Length});
   check("scrolling moves highlight geometry with the text",after.Count==before.Length&&after[0].Top<before[0].Top-100,new{before=before[0].Top,after=after[0].Top});
  }
  finally{window.Close();}
 }
 static Run Find(ReadingContent reader,string text)
 {
  var p=reader.Document.ContentStart;
  while(p is not null&&p.CompareTo(reader.Document.ContentEnd)<0)
  {
   if(p.GetPointerContext(LogicalDirection.Forward)==TextPointerContext.ElementStart&&p.GetAdjacentElement(LogicalDirection.Forward) is Run run&&run.Text==text)return run;
   p=p.GetNextContextPosition(LogicalDirection.Forward);
  }
  throw new InvalidOperationException("Text run not found.");
 }
 public static void SelectSnippet(ReadingContent reader,string snippet)
 {
  var p=reader.Document.ContentStart;
  while(p is not null&&p.CompareTo(reader.Document.ContentEnd)<0)
  {
   if(p.GetPointerContext(LogicalDirection.Forward)==TextPointerContext.ElementStart&&p.GetAdjacentElement(LogicalDirection.Forward) is Run run)
   {
    int at=run.Text.IndexOf(snippet,StringComparison.Ordinal);
    if(at>=0){reader.Selection.Select(run.ContentStart.GetPositionAtOffset(at)!,run.ContentStart.GetPositionAtOffset(at+snippet.Length)!);return;}
   }
   p=p.GetNextContextPosition(LogicalDirection.Forward);
  }
  throw new InvalidOperationException("Snippet not found.");
 }
 static void Select(ReadingContent reader,string text,int start,int length)
 {
  var run=Find(reader,text);reader.Selection.Select(run.ContentStart.GetPositionAtOffset(start)!,run.ContentStart.GetPositionAtOffset(start+length)!);
 }
 static void Save(Window window,string path)
 {
  var visual=(FrameworkElement)window.Content;visual.UpdateLayout();
  var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
  // Adorners sit in the Window's AdornerDecorator, outside its Content visual.
  var decorator=FindDecorator(window);if(decorator is not null)bitmap.Render(decorator);
  File.WriteAllBytes(path,Capture.Png(bitmap));
 }
 static AdornerDecorator? FindDecorator(DependencyObject item)
 {
  if(item is AdornerDecorator match)return match;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++){var found=FindDecorator(VisualTreeHelper.GetChild(item,i));if(found is not null)return found;}
  return null;
 }
}

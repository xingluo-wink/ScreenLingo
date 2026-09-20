using ScreenLingo.Core;
namespace ScreenLingo.Tests;

static class LayoutGroupingTests
{
 public static void Run(Action<string,bool,object?> check)
 {
  void Whole(string name,OcrLine[] lines,string expected)
  {
   var result=LayoutGrouper.Group(lines);check(name,result.Count==1&&result[0].Text==expected,result);
  }
  Whole("uneven OCR boxes become one complete selection",
   [new("The patient traveller would never leave",16,3,1251,63,1),new("a trusted companion behind just to choose",15,58,1283,54,1),new("the first new path that came into view.",13,106,843,57,1)],
   "The patient traveller would never leave a trusted companion behind just to choose the first new path that came into view.");
  Whole("shuffled OCR retains top-to-bottom order",
   [new("before reaching the next destination.",0,74,430,40,1),new("This continuous paragraph must be read",0,0,650,40,1),new("from the first line through the middle",0,40,670,32,1)],
   "This continuous paragraph must be read from the first line through the middle before reaching the next destination.");
  Whole("headings, gaps and numbered items do not split the selection",
   [new("A heading",0,0,300,40,1),new("1. Read the first item.",0,80,500,24,1),new("2. Then read the second item.",0,180,500,24,1)],
   "A heading 1. Read the first item. 2. Then read the second item.");
  Whole("same-row fragments use left-to-right order",
   [new("right",320,0,100,24,1),new("left",0,0,100,24,1),new("below",0,40,100,24,1)],"left right below");
  Whole("Chinese wrapping has no inserted punctuation spaces",
   [new("这是一段连续的中文内容，",0,0,310,24,1),new("句号也不代表段落结束。",0,29,290,24,1),new("下一句仍然属于同一段。",0,58,290,24,1)],
   "这是一段连续的中文内容，句号也不代表段落结束。下一句仍然属于同一段。");
  Whole("English word split across OCR lines is rejoined",
   [new("An inter-",0,0,200,24,1),new("national project.",0,29,300,24,1)],"An international project.");
  check("empty OCR does not create a translation request",LayoutGrouper.Group([new(" ",0,0,20,20,1)]).Count==0,null);
  var large=LayoutGrouper.Group(Enumerable.Range(1,100).Select(i=>new OcrLine($"Line {i}.",0,i*60,500,24,1)));
  check("large selection remains one unit without omissions",large.Count==1&&large[0].Text==string.Join(" ",Enumerable.Range(1,100).Select(i=>$"Line {i}.")),null);
 }
}

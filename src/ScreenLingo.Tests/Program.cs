using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLingo;
using ScreenLingo.Core;
namespace ScreenLingo.Tests;
static class Program
{
 static readonly List<object> results=[];static int failures;
 [STAThread]static int Main(string[] args)
 {
  string root=args.Length>0?args[0]:Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));Directory.CreateDirectory(Path.Combine(root,"work","qa"));
  if(args.Contains("--reader-layout"))
  {
   Ui.Install(new Application());ReaderLayoutTests();ReaderFeatureTests(root);File.WriteAllText(Path.Combine(root,"work","qa","reader-layout-report.json"),JsonSerializer.Serialize(new{passed=results.Count-failures,failed=failures,results},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
  }
  if(args.Contains("--fixture"))
  {
   Fixtures(root);var bitmap=new BitmapImage(new Uri(Path.Combine(root,"work","qa","desktop-light.png")));
   new Application().Run(new Window{Title="ScreenLingo QA fixture",Width=1240,Height=800,Content=new System.Windows.Controls.Image{Source=bitmap,Stretch=Stretch.Uniform},WindowStartupLocation=WindowStartupLocation.CenterScreen});return 0;
  }
  try
  {
   CoreTests();Fixtures(root);ApiTests().GetAwaiter().GetResult();OcrTests(root).GetAwaiter().GetResult();
  }
  catch(Exception ex){Check("unhandled",false,ex.ToString());}
  File.WriteAllText(Path.Combine(root,"work","qa","test-report.json"),JsonSerializer.Serialize(new{passed=results.Count-failures,failed=failures,results},new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine($"TESTS: {results.Count-failures} passed, {failures} failed");return failures==0?0:1;
 }
 static void Check(string name,bool passed,object? detail=null){results.Add(new{name,passed,detail});if(!passed)failures++;Console.WriteLine($"{(passed?"PASS":"FAIL")} {name}: {JsonSerializer.Serialize(detail)}");}
 static void Throws(string name,Action action){try{action();Check(name,false);}catch(Exception){Check(name,true);}}
 static TextRegion Block(string id,string text)=>new(id,text,0,0,600,30,24,1);
 static void ReaderLayoutTests()
 {
  var selection=new System.Drawing.Rectangle(150,200,150,45);var screen=new System.Drawing.Rectangle(0,0,1920,1040);
  double Measure(double width,string content){var text=Ui.Text(content,16);text.LineHeight=25;var card=Ui.Card(text,new Thickness(12,9,12,9));card.Margin=new Thickness(0,0,0,8);card.Measure(new Size(width,double.PositiveInfinity));return card.DesiredSize.Height;}
  const string shortText="In-place Chinese overlay";
  var small=ReadingLayout.Fit(selection,screen,1,1,100,w=>Measure(w,shortText));
  Check("small selection shows complete translation",small.Width>=420&&small.Height>=Measure(small.Width-28,shortText)+28,new{small.Width,small.Height});
  string paragraph=string.Join(" ",Enumerable.Repeat("The full translated content must remain visible without opening another dialog.",12));
  var longText=ReadingLayout.Fit(selection,screen,1,1,100,w=>Measure(w,paragraph));
  Check("paragraph grows vertically to fit",longText.Height>small.Height&&longText.Height>=Measure(longText.Width-28,paragraph)+28,new{longText.Width,longText.Height});
  var monitor=new System.Drawing.Rectangle(-1920,0,1920,1040);
  var edge=ReadingLayout.Fit(new(-50,990,150,45),monitor,1.5,1.5,150,w=>Measure(w,paragraph));
  Check("expanded panel stays within monitor",edge.Left>=monitor.Left&&edge.Right<=monitor.Right&&edge.Bottom<=monitor.Bottom&&edge.Top>=monitor.Top+150,new{edge.X,edge.Y,edge.Width,edge.Height});
 }
 static void ReaderFeatureTests(string root)
 {
  const string zhText="原位中文覆盖。字号变大以后，完整内容也应自动换行，不需要点开另一个窗口才能看清。";
  const string enText="In-place Chinese overlay. Larger text should wrap naturally, and the entire paragraph should remain readable without opening another window.";
  var region=Block("b1","原位中文覆盖 · In-place overlay");
  var zh=new Dictionary<string,string>{{"b1",zhText}};var en=new Dictionary<string,string>{{"b1",enText}};
  System.Windows.Controls.Border Pair(int font)
  {
   var pair=ReadingContent.Card(region,zh,en,"zh",true,font);
   System.Windows.Documents.TextElement.SetFontFamily(pair,new FontFamily("Microsoft YaHei UI"));return pair;
  }
  var normal=Pair(16);normal.Measure(new Size(732,double.PositiveInfinity));
  var large=Pair(32);large.Measure(new Size(732,double.PositiveInfinity));large.Arrange(new Rect(new Point(),large.DesiredSize));large.UpdateLayout();
  var columns=(System.Windows.Controls.Grid)large.Child;
  var left=(System.Windows.Controls.StackPanel)columns.Children[0];var right=(System.Windows.Controls.StackPanel)columns.Children[2];
  var leftText=(System.Windows.Controls.TextBlock)left.Children[1];var rightText=(System.Windows.Controls.TextBlock)right.Children[1];
  Check("both full languages visible side by side",leftText.Text==zhText&&rightText.Text==enText&&left.ActualWidth>250&&right.ActualWidth>250&&right.TranslatePoint(new Point(),columns).X>left.ActualWidth);
  Check("larger font grows cards without shrinking text",leftText.FontSize==32&&rightText.FontSize==32&&large.DesiredSize.Height>normal.DesiredSize.Height,new{normal=normal.DesiredSize.Height,large=large.DesiredSize.Height});
  var screen=new System.Drawing.Rectangle(0,0,1920,1040);
  var fitted=ReadingLayout.Fit(new(150,200,150,45),screen,1,1,140,width=>{large.Measure(new Size(width,double.PositiveInfinity));return large.DesiredSize.Height;},true);
  Check("small capture expands for complete bilingual content",fitted.Width>=760&&fitted.Height>=large.DesiredSize.Height+28,new{fitted.Width,fitted.Height});
  var scroll=new System.Windows.Controls.ScrollViewer{Padding=new Thickness(14),VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,Content=large};
  scroll.Measure(new Size(fitted.Width,fitted.Height));scroll.Arrange(new Rect(0,0,fitted.Width,fitted.Height));scroll.UpdateLayout();
  Check("fitted bilingual reader has no hidden short content",scroll.ScrollableHeight==0&&scroll.ScrollableWidth==0,new{scroll.ScrollableHeight,scroll.ScrollableWidth});
  scroll.Content=null;
  var copy=ReadingContent.BilingualText([region],zh,en);Check("copy keeps complete paired text",copy.Contains(zhText)&&copy.Contains(enText));
  var missing=ReadingContent.Card(region,zh,new Dictionary<string,string>(),"zh",true,16);
  var missingRight=(System.Windows.Controls.StackPanel)((System.Windows.Controls.Grid)missing.Child).Children[2];
  Check("missing language is not mislabeled source text",((System.Windows.Controls.TextBlock)missingRight.Children[1]).Text=="尚未翻译");
  var stack=new System.Windows.Controls.StackPanel();for(int i=0;i<4;i++)stack.Children.Add(Pair(32));
  var export=new System.Windows.Controls.Border{Width=760,Padding=new Thickness(14),Background=Ui.Background,Child=stack};
  export.Measure(new Size(760,double.PositiveInfinity));export.Arrange(new Rect(new Point(),export.DesiredSize));export.UpdateLayout();
  Check("export includes content beyond screen height",export.ActualHeight>screen.Height&&stack.Children.Cast<FrameworkElement>().All(x=>x.ActualHeight>0),new{export.ActualHeight});
  var bitmap=new RenderTargetBitmap((int)Math.Ceiling(export.ActualWidth),(int)Math.Ceiling(export.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(export);
  File.WriteAllBytes(Path.Combine(root,"work","qa","bilingual-font-32.png"),Capture.Png(bitmap));
  var store=new SettingsStore(Path.Combine(root,"work","qa","display-settings"));
  File.WriteAllText(store.FilePath,"{\"Hotkey\":\"Ctrl+Alt+Q\",\"Profiles\":[{\"Name\":\"Mock profile\",\"ProtectedKey\":\"test-only-protected-value\"}]}");
  var settings=store.Load();Check("older settings keep safe display defaults",settings.ReadingFontSize==16&&!settings.BilingualDisplay);
  settings.ReadingFontSize=32;settings.BilingualDisplay=true;store.Save(settings);var restored=store.Load();
  Check("display preferences persist without altering API fields",restored.ReadingFontSize==32&&restored.BilingualDisplay&&restored.Profiles[0].ProtectedKey=="test-only-protected-value"&&restored.Profiles[0].Name=="Mock profile");
  settings.ReadingFontSize=500;store.Save(settings);Check("invalid saved font is bounded",store.Load().ReadingFontSize==40);
 }
 static void CoreTests()
 {
  var wrapped=LayoutGrouper.Group([new("You should take the following",0,0,380,24,1),new("factors into account.",0,29,300,24,1)]);
  Check("join wrapped sentence",wrapped.Count==1&&wrapped[0].Text=="You should take the following factors into account.");
  Check("separate heading from body",LayoutGrouper.Group([new("Learn something new every day",27,27,463,44,1),new("Practice makes progress.",28,89,291,36,1)]).Count==2);
  var columns=LayoutGrouper.Group([new("This is a very long left column",0,0,350,24,1),new("Right column paragraph starts",500,0,350,24,1),new("continued on the left.",0,30,320,24,1),new("continued on the right.",500,30,320,24,1)]);
  Check("separate columns",columns.Count==2&&!columns[0].Text.Contains("Right"));
  var labels=LayoutGrouper.Group([new("Apply",0,0,60,20,1),new("Cancel",0,25,70,20,1)]);Check("do not merge menus",labels.Count==2);
  var parsed=TranslationService.ParseTranslations("```json\n{\"translations\":[{\"id\":\"b2\",\"text\":\"二\"},{\"id\":\"b1\",\"text\":\"一\"}]}\n```",["b1","b2"]);Check("IDs survive reordering",parsed["b1"]=="一");
  Throws("reject missing translation",()=>TranslationService.ParseTranslations("{\"translations\":[]}",["b1"]));
  Throws("reject duplicate IDs",()=>TranslationService.ParseTranslations("[{\"id\":\"b1\",\"text\":\"x\"},{\"id\":\"b1\",\"text\":\"y\"}]",["b1","b2"]));
  Throws("reject empty translation",()=>TranslationService.ParseTranslations("[{\"id\":\"b1\",\"text\":\"\"}]",["b1"]));
  Throws("reject insecure remote endpoint",()=>TranslationService.GetEndpoint(new(){BaseUrl="http://example.com/v1",Model="test"}));
  Check("endpoint avoids duplicate suffix",TranslationService.GetEndpoint(new(){BaseUrl="https://example.com/v1/chat/completions/",Model="x"}).AbsoluteUri=="https://example.com/v1/chat/completions");
  Check("localhost allowed",TranslationService.GetEndpoint(new(){BaseUrl="http://localhost:8723/v1",Model="x"}).IsLoopback);
  string encrypted=KeyVault.Protect("test-only-key-123");Check("DPAPI roundtrip",encrypted!="test-only-key-123"&&KeyVault.Unprotect(encrypted)=="test-only-key-123");
  Throws("invalid hotkey",()=>Native.ParseHotkey("Q"));Throws("reserved F12",()=>Native.ParseHotkey("Ctrl+F12"));
 }
 static async Task ApiTests()
 {
  foreach(var protocol in Enum.GetValues<ApiProtocol>())
  {
   var handler=new FakeHandler(protocol);using var service=new TranslationService(handler);
   var result=await service.TranslateAsync(new(){BaseUrl="https://example.com/v1",Model="user-model",Protocol=protocol,ExtraBody="{\"temperature\":0.2,\"stream\":true}"},"test-only",[Block("b1","Hello world")],"zh",null,CancellationToken.None);
   Check("API "+protocol,result["b1"]=="你好，世界"&&handler.Valid,handler.Path);
  }
  using(var service=new TranslationService(new FakeHandler(ApiProtocol.ChatCompletions,HttpStatusCode.TooManyRequests)))
  {try{await service.TranslateAsync(new(){BaseUrl="https://example.com/v1",Model="x"},"",[Block("b1","x")],"zh",null,CancellationToken.None);Check("rate limit",false);}catch(HttpRequestException ex){Check("rate limit",ex.Message.Contains("429"));}}
  using(var service=new TranslationService(new FakeHandler(ApiProtocol.ChatCompletions,delay:true)))
  {using var cts=new CancellationTokenSource(50);try{await service.TranslateAsync(new(){BaseUrl="https://example.com/v1",Model="x"},"",[Block("b1","x")],"zh",null,cts.Token);Check("cancel API",false);}catch(OperationCanceledException){Check("cancel API",true);}}
 }
 sealed class FakeHandler(ApiProtocol protocol,HttpStatusCode code=HttpStatusCode.OK,bool delay=false):HttpMessageHandler
 {
  public bool Valid;public string? Path;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
   if(delay)await Task.Delay(5000,token);Path=request.RequestUri!.AbsolutePath;
   using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=body.RootElement;
   Valid=!root.TryGetProperty("stream",out var stream)||stream.ValueKind==JsonValueKind.False;
   Valid&=protocol==ApiProtocol.Gemini?request.Headers.Contains("x-goog-api-key"):protocol==ApiProtocol.Anthropic?request.Headers.Contains("x-api-key"):request.Headers.Authorization?.Scheme=="Bearer";
   string translated=protocol==ApiProtocol.QwenMt?"你好，世界":"{\"translations\":[{\"id\":\"b1\",\"text\":\"你好，世界\"}]}";
   object data=protocol switch
   {
    ApiProtocol.Responses=>new{status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=translated}}}}},
    ApiProtocol.Anthropic=>new{content=new[]{new{type="text",text=translated}}},
    ApiProtocol.Gemini=>new{candidates=new[]{new{content=new{parts=new[]{new{text=translated}}}}}},
    _=>new{choices=new[]{new{finish_reason="stop",message=new{content=translated}}}}
   };
   return new(code){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
  }
 }
 static void Fixtures(string root)
 {
  var fixtureDir=Path.Combine(root,"work","qa");
  Draw(Path.Combine(fixtureDir,"desktop-light.png"),false);Draw(Path.Combine(fixtureDir,"desktop-dark.png"),true);
  var white=new DrawingVisual();using(var dc=white.RenderOpen())dc.DrawRectangle(Brushes.White,null,new Rect(0,0,800,400));Save(white,800,400,Path.Combine(fixtureDir,"blank.png"));
 }
 static void Draw(string path,bool dark)
 {
  var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
  {
   dc.DrawRectangle(dark?new SolidColorBrush(Color.FromRgb(28,34,41)):Brushes.White,null,new Rect(0,0,1200,720));
   void Text(string s,double x,double y,int size)=>dc.DrawText(new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,dark?Brushes.White:Brushes.Black,1),new Point(x,y));
   Text("Learn something new every day",32,28,30);Text("Practice makes progress.",32,90,24);Text("Read a little, then try it yourself.",32,136,24);
   Text("每天学习一点新知识",32,208,28);Text("选择区域后，可以切换中文和英文。",32,258,24);Text("保存截图是独立功能。",32,304,24);
   Text("Settings",720,90,22);Text("Apply",720,132,20);Text("Cancel",840,132,20);Text("File  Edit  View  Help",32,380,18);
   Text("The answer is 42. Version 1.2.3",32,428,18);Text("Small text remains readable.",32,475,14);Text("CPU: 12%    Memory: 256 MB",32,520,20);
  }
  Save(visual,1200,720,path);
 }
 static void Save(Visual visual,int width,int height,string path){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);File.WriteAllBytes(path,Capture.Png(bitmap));}
 static async Task OcrTests(string root)
 {
  using var ocr=new OcrClient(Path.Combine(root,"release","ScreenLingo"));var timings=new List<double>();var memories=new List<double>();
  foreach(string file in new[]{"desktop-light.png","desktop-dark.png","blank.png"})
  {
   var clock=Stopwatch.StartNew();var response=await ocr.RecognizeAsync(await File.ReadAllBytesAsync(Path.Combine(root,"work","qa",file)),CancellationToken.None);
   string text=string.Join("\n",response.Lines.Select(l=>l.Text));File.WriteAllText(Path.Combine(root,"work","qa",file+".json"),JsonSerializer.Serialize(response,new JsonSerializerOptions{WriteIndented=true}));
   Check("OCR "+file,file=="blank.png"?response.Lines.Count==0:text.Contains("Practice makes progress")&&text.Contains("每天学习一点新知识")&&text.Contains("Settings"),new{milliseconds=clock.Elapsed.TotalMilliseconds,lines=response.Lines.Count,text});
  }
  byte[] sample=await File.ReadAllBytesAsync(Path.Combine(root,"work","qa","desktop-light.png"));
  bool consistent=true;
  for(int i=0;i<20;i++)
  {
   var clock=Stopwatch.StartNew();var result=await ocr.RecognizeAsync(sample,CancellationToken.None);timings.Add(clock.Elapsed.TotalMilliseconds);
   consistent &= result.Lines.Count==13&&result.Lines.Any(l=>l.Text.Contains("每天学习一点新知识"));
   using var worker=Process.GetProcessById(ocr.WorkerPid!.Value);memories.Add(worker.WorkingSet64/1048576d);
  }
  Check("OCR 20 consecutive captures",consistent,new{medianMs=timings.Order().ElementAt(10),minMemoryMiB=memories.Min(),maxMemoryMiB=memories.Max(),lastMemoryMiB=memories.Last()});
  int oldPid=ocr.WorkerPid!.Value;ocr.ReleaseNow();await Task.Delay(250);bool gone;try{using var old=Process.GetProcessById(oldPid);gone=old.HasExited;}catch(ArgumentException){gone=true;}
  Check("worker memory reclaimed",gone&&!ocr.IsLoaded);
  using var canceled=new CancellationTokenSource();canceled.Cancel();try{await ocr.RecognizeAsync(sample,canceled.Token);Check("cancelled OCR",false);}catch(OperationCanceledException){Check("cancelled OCR",true);}
  await ocr.RecognizeAsync(sample,CancellationToken.None);ocr.IdleSeconds=1;
  for(int i=0;i<24&&ocr.IsLoaded;i++)await Task.Delay(500);
  Check("idle worker exits automatically",!ocr.IsLoaded);
  var restarted=await ocr.RecognizeAsync(sample,CancellationToken.None);Check("OCR restarts after idle",restarted.Lines.Count==13);
  using var inFlight=new CancellationTokenSource(20);try{await ocr.RecognizeAsync(sample,inFlight.Token);Check("cancel in-flight OCR",false);}catch(OperationCanceledException){Check("cancel in-flight OCR",!ocr.IsLoaded);}
 }
}

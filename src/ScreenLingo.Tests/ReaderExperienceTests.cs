using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenLingo.Core;
namespace ScreenLingo.Tests;

static class ReaderExperienceTests
{
 public static void Run(Action<string,bool,object?> check,string root)
 {
  var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
  var task=RunAsync(check,root);var frame=new DispatcherFrame();
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>{if(task.IsCompleted)frame.Continue=false;};timer.Start();
  Dispatcher.PushFrame(frame);timer.Stop();SynchronizationContext.SetSynchronizationContext(previous);task.GetAwaiter().GetResult();
 }
 static async Task RunAsync(Action<string,bool,object?> check,string root)
 {
  var work=Path.Combine(root,"work","qa");Directory.CreateDirectory(work);
  var monitor=new System.Drawing.Rectangle(-1920,0,1920,1040);
  var initial=ReadingLayout.Initial(new(-80,960,80,30),monitor,1.5,1.5,0,0,false);
  check("small selection creates usable window inside mixed-DPI monitor",initial.Width>=810&&initial.Left>=monitor.Left&&initial.Right<=monitor.Right&&initial.Bottom<=monitor.Bottom,initial);
  check("narrow or large-font bilingual content falls back to stacked paragraphs",!ReadingLayout.UseColumns(true,640,16)&&!ReadingLayout.UseColumns(true,780,40)&&ReadingLayout.UseColumns(true,840,24),null);
  var store=new SettingsStore(Path.Combine(work,"reader-settings"));
  File.WriteAllText(store.FilePath,"{\"ReadingFontSize\":18,\"BilingualDisplay\":true,\"Profiles\":[{\"Name\":\"Mock API\",\"ProtectedKey\":\"test-only-value\"}]}");
  var settings=store.Load();check("existing API and display settings migrate",settings.ReadingFontSize==18&&settings.BilingualDisplay&&settings.ReaderTopmost&&!settings.BilingualSideBySide&&settings.Profiles[0].StreamResponses&&settings.Profiles[0].ProtectedKey=="test-only-value",null);
  settings.ReaderWidth=730;settings.ReaderHeight=420;settings.ReaderTopmost=false;store.Save(settings);var restored=store.Load();
  check("window preferences round-trip with credentials unchanged",restored.ReaderWidth==730&&restored.ReaderHeight==420&&!restored.ReaderTopmost&&restored.Profiles[0].ProtectedKey=="test-only-value",null);

  var host=new FakeHost(20);var window=NewWindow(host);window.Show();
  try
  {
   await Ready(window);check("one native movable and resizable reading window",window.WindowStyle==WindowStyle.SingleBorderWindow&&window.ResizeMode==ResizeMode.CanResize&&window.OwnedWindows.Count==0,null);
   check("recognized text appears before API requests",window.Reader.PlainText.Contains("Original 1")&&host.Requests.Count==0,null);
   window.Left=230;window.Top=170;window.Width=740;window.Height=430;await Task.Delay(30);window.UseManualBounds();var bounds=Native.ReaderBounds(window);
   await window.SelectModeAsync(ReadingMode.Bilingual);await Task.Delay(30);
   check("both complete languages appear in one selectable document",window.Reader.PlainText.Contains("EN b20")&&window.Reader.PlainText.Contains("中文 b20")&&!window.Reader.PlainText.Contains("尚无译文"),null);
   int calls=host.Requests.Count;check("whole selection translates before separate compact alignment",calls==2&&host.Requests[0].Ids.Length==1&&host.Requests[0].Input.Contains("Original 20.")&&host.Requests[1].Language=="align",new{calls});
   LinkedSelectionTests.SelectSnippet(window.Reader,"中文 b1。");
   check("reader uses returned alignment without sending selection requests",window.Reader.LinkedSelectionLabel=="对应英文：EN b1."&&host.Requests.Count==calls,null);
   window.Reader.SelectAll();string selected=window.Reader.Selection.Text;var document=window.Reader.Document;
   window.SetReadingFontSize(32);await Task.Delay(30);
   check("font adjustment preserves document and selection",ReferenceEquals(document,window.Reader.Document)&&window.Reader.Selection.Text==selected&&window.Reader.Document.FontSize==32,null);
   window.ToggleOriginal();window.ToggleOriginal();await Task.Delay(20);
   check("font and original toggles preserve user window bounds without API calls",Native.ReaderBounds(window)==bounds&&host.Requests.Count==calls,new{bounds,actual=Native.ReaderBounds(window)});
   window.Reader.Selection.Select(window.Reader.Document.ContentStart,window.Reader.Document.ContentStart);window.Reader.ScrollToEnd();await Task.Delay(30);
   double offset=window.Reader.VerticalOffset;window.SetReadingFontSize(32);await Task.Delay(30);
   check("ordinary updates preserve reading position",offset>0&&Math.Abs(window.Reader.VerticalOffset-offset)<3,new{offset,after=window.Reader.VerticalOffset});
   var export=window.CreateExportContent(window.Reader.ActualWidth);
   check("export contains the entire long document beyond the viewport",export.ActualHeight>window.Reader.ActualHeight*3&&export.PlainText.Contains("EN b20")&&export.PlainText.Contains("中文 b20"),new{export.ActualHeight,viewport=window.Reader.ActualHeight});
   await window.SelectModeAsync(ReadingMode.Chinese);await window.SelectModeAsync(ReadingMode.English);await window.SelectModeAsync(ReadingMode.Bilingual);
   check("language-mode switches reuse all completed translations",host.Requests.Count==calls&&window.CurrentMode==ReadingMode.Bilingual,null);
   check("translation completion never resets a manually placed window",Native.ReaderBounds(window)==bounds&&window.HasManualBounds,null);
   await window.SelectModeAsync(ReadingMode.Chinese);window.ShowLayoutPreview();
   check("overflowing original layout falls back to complete reading",window.Reader.Visibility==Visibility.Visible&&window.Reader.PlainText.Contains("中文 b20"),null);
  }
  finally{window.Close();}

  var partialHost=new FakeHost(20){SlowSecond=true};var partial=NewWindow(partialHost);partial.Show();
  try
  {
   await Ready(partial);await partial.SelectModeAsync(ReadingMode.Chinese);var translation=partial.SelectModeAsync(ReadingMode.Bilingual);
   await Until(()=>partialHost.Requests.Count>=2);partial.CancelTranslation();await translation;
   check("cancel bilingual generation preserves the complete existing translation",!partial.IsTranslating&&partial.Reader.PlainText.Contains("中文 b20")&&!partial.Reader.PlainText.Contains("EN b20"),null);
   partialHost.SlowSecond=false;int before=partialHost.Requests.Count;await partial.TranslateAsync();
   check("retry reuses the existing language before adding alignment",partialHost.Requests.Count==before+2&&partialHost.Requests[^2].Ids.Length==1&&partialHost.Requests[^2].KnownChinese&&partialHost.Requests[^1].Language=="align"&&partial.Reader.PlainText.Contains("EN b20"),null);
  }
  finally{partial.Close();}

  var limitedHost=new FakeHost(3){TruncateAlignment=2,SlowAlignment=true};var limited=NewWindow(limitedHost);limited.Show();
  try
  {
   await Ready(limited);var translating=limited.SelectModeAsync(ReadingMode.Bilingual);
   await Until(()=>limitedHost.Requests.Any(r=>r.Language=="align"));
   check("complete bilingual text is visible while alignment is pending",limited.IsTranslating&&limited.Reader.PlainText.Contains("EN b3")&&limited.Reader.PlainText.Contains("中文 b3"),null);
   await translating;
   check("repeated alignment truncation keeps both complete texts",!limited.IsTranslating&&limited.Reader.PlainText.Contains("EN b3")&&limited.Reader.PlainText.Contains("中文 b3")&&limitedHost.Requests.Count==3,null);
   Save((FrameworkElement)limited.Content,Path.Combine(work,"reader-0.3.1-truncated-alignment.png"));
   limitedHost.SlowAlignment=false;int before=limitedHost.Requests.Count;await limited.TranslateAsync();
   LinkedSelectionTests.SelectSnippet(limited.Reader,"中文 b1。");
   check("retry after truncation requests alignment only",limitedHost.Requests.Count==before+1&&limitedHost.Requests[^1].Language=="align"&&limited.Reader.LinkedSelectionLabel=="对应英文：EN b1.",null);
  }
  finally{limited.Close();}

  var stopHost=new FakeHost(3){SlowAlignment=true};var stop=NewWindow(stopHost);stop.Show();
  try
  {
   await Ready(stop);var pending=stop.SelectModeAsync(ReadingMode.Bilingual);await Until(()=>stopHost.Requests.Any(r=>r.Language=="align"));stop.CancelTranslation();await pending;
   check("cancelling optional alignment preserves both complete translations",!stop.IsTranslating&&stop.Reader.PlainText.Contains("EN b3")&&stop.Reader.PlainText.Contains("中文 b3"),null);
  }
  finally{stop.Close();}

  var raceHost=new FakeHost(3){Delay=140};var race=NewWindow(raceHost);race.Show();
  try
  {
   await Ready(race);var old=race.SelectModeAsync(ReadingMode.Chinese);await Until(()=>raceHost.Requests.Count>0);await race.SelectModeAsync(ReadingMode.Chinese);
   check("repeated active-mode clicks do not restart the request",raceHost.Requests.Count==1,null);
   check("original remains readable while waiting for the first translation",race.Reader.PlainText.Contains("Original 1"),null);
   var current=race.SelectModeAsync(ReadingMode.English);await Task.WhenAll(old,current);await Task.Delay(40);
   check("late cancelled response cannot overwrite the current mode",race.CurrentMode==ReadingMode.English&&race.Reader.PlainText.Contains("EN b3")&&!race.Reader.PlainText.Contains("中文 b3"),null);
   raceHost.Settings.Profiles[0].Model="changed-model";var closing=race.TranslateAsync();await Task.Delay(15);race.Close();await closing;
   check("closing during translation cancels cleanly",!race.IsVisible,null);
  }
  finally{if(race.IsVisible)race.Close();}

  var sampleHost=new FakeHost(1);var sample=NewWindow(sampleHost);sample.Show();
  try
  {
   await Ready(sample);await sample.SelectModeAsync(ReadingMode.Bilingual);await Task.Delay(40);
   check("short bilingual result fits without internal clipping",sample.Reader.ExtentHeight<=sample.Reader.ViewportHeight+2,new{sample.Reader.ExtentHeight,sample.Reader.ViewportHeight});
   Save((FrameworkElement)sample.Content,Path.Combine(work,"reader-0.3.0-stacked.png"));
   sample.Width=950;sample.Height=450;sample.UseManualBounds();sampleHost.Settings.BilingualSideBySide=true;sample.SetReadingFontSize(20);await Task.Delay(40);
   check("wide bilingual layout contains both columns",sample.Reader.UsesColumns&&sample.Reader.Document.Blocks.FirstBlock is Table&&sample.Reader.PlainText.Contains("EN b1")&&sample.Reader.PlainText.Contains("中文 b1"),null);
   Save((FrameworkElement)sample.Content,Path.Combine(work,"reader-0.3.0-columns.png"));
   sample.Width=550;await Task.Delay(50);check("resizing narrow safely returns to stacked bilingual text",!sample.Reader.UsesColumns&&sample.Reader.PlainText.Contains("中文 b1"),null);
   sample.SetReadingFontSize(40);var large=sample.CreateExportContent(sample.Reader.ActualWidth);Save(large,Path.Combine(work,"reader-0.3.0-font40.png"));
   check("large-font export includes final characters",large.PlainText.EndsWith("末尾完整。")&&large.ActualHeight>100,null);
  }
  finally{sample.Close();}
  var streamHost=new FakeHost(3){Streaming=true};var streaming=NewWindow(streamHost);streaming.Show();
  try
  {
   await Ready(streaming);var pending=streaming.SelectModeAsync(ReadingMode.Bilingual);
   await Until(()=>streaming.Reader.PlainText.Contains("EN b1"));
   check("reader displays streamed text before response completion",streaming.IsTranslating&&!pending.IsCompleted&&!streaming.Reader.PlainText.Contains("中文 b3"),null);
   streamHost.TextGate.TrySetResult();await Until(()=>streamHost.Requests.Any(r=>r.Language=="align"));
   LinkedSelectionTests.SelectSnippet(streaming.Reader,"中文 b1。");await Until(()=>streaming.Reader.LinkedSelectionLabel.Length>0);
   check("reader links completed streamed pairs before alignment finishes",streaming.IsTranslating&&streaming.Reader.LinkedSelectionLabel=="对应英文：EN b1."&&streaming.Reader.LinkedHighlightRects.Count>0,null);
   Save((FrameworkElement)streaming.Content,Path.Combine(work,"reader-0.3.2-progressive.png"));
   streamHost.AlignmentGate.TrySetResult();await pending;int calls=streamHost.Requests.Count;
   await streaming.SelectModeAsync(ReadingMode.Chinese);await streaming.SelectModeAsync(ReadingMode.Bilingual);
   check("only validated streamed results are reused by mode switches",streaming.Reader.PlainText.Contains("中文 b3")&&streamHost.Requests.Count==calls,null);
  }
  finally{streaming.Close();}
  var streamCancelHost=new FakeHost(3){Streaming=true};var streamCancel=NewWindow(streamCancelHost);streamCancel.Show();
  try
  {
   await Ready(streamCancel);await streamCancel.SelectModeAsync(ReadingMode.Chinese);var pending=streamCancel.SelectModeAsync(ReadingMode.Bilingual);
   await Until(()=>streamCancel.Reader.PlainText.Contains("EN b1"));streamCancel.CancelTranslation();await pending;
   check("cancel discards streaming draft but keeps completed translation",!streamCancel.Reader.PlainText.Contains("EN b1")&&streamCancel.Reader.PlainText.Contains("中文 b3"),null);
   streamCancelHost.TextGate.TrySetResult();streamCancelHost.AlignmentGate.TrySetResult();int calls=streamCancelHost.Requests.Count;await streamCancel.TranslateAsync();
   check("cancelled streaming text is requested again instead of cached",streamCancelHost.Requests.Count==calls+2&&streamCancel.Reader.PlainText.Contains("EN b3"),null);
  }
  finally{streamCancel.Close();}
  var partialLinksHost=new FakeHost(3){TruncateAlignment=1,PartialPairs=true};var partialLinks=NewWindow(partialLinksHost);partialLinks.Show();
  try
  {
   await Ready(partialLinks);await partialLinks.SelectModeAsync(ReadingMode.Bilingual);
   LinkedSelectionTests.SelectSnippet(partialLinks.Reader,"中文 b1。");
   check("truncated alignment retains useful highlights and complete text",partialLinks.Reader.LinkedSelectionLabel=="对应英文：EN b1."&&partialLinks.Reader.PlainText.Contains("中文 b3")&&partialLinksHost.Requests.Count==2,null);
   int calls=partialLinksHost.Requests.Count;await partialLinks.TranslateAsync();LinkedSelectionTests.SelectSnippet(partialLinks.Reader,"中文 b3。");
   check("partial alignment can be retried without retranslating text",partialLinksHost.Requests.Count==calls+1&&partialLinksHost.Requests[^1].Language=="align"&&partialLinks.Reader.LinkedSelectionLabel=="对应英文：EN b3.",null);
  }
  finally{partialLinks.Close();}
  await LinkedSelectionTests.Run(check,work);
 }
 static OverlayWindow NewWindow(FakeHost host)
 {
  var image=BitmapSource.Create(1200,1000,96,96,PixelFormats.Bgr32,null,new byte[1200*1000*4],1200*4);image.Freeze();
  return new(host,new(image,new(140,170,180,45))){ShowActivated=false,ShowInTaskbar=false,Opacity=0};
 }
 static Task Ready(OverlayWindow window)=>Until(()=>window.Reader.PlainText.Contains("Original 1"));
 static async Task Until(Func<bool> condition)
 {
  var start=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(start.Elapsed.TotalSeconds>8)throw new TimeoutException("Reader scenario timed out.");await Task.Delay(10);}
 }
 static void Save(FrameworkElement visual,string path)
 {
  visual.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);File.WriteAllBytes(path,Capture.Png(bitmap));
 }
 sealed record Request(string Language,string[] Ids,string Input,bool KnownChinese=false);
 sealed class FakeHost(int count):IReaderHost
 {
  public AppSettings Settings{get;}=new(){Profiles=[new(){BaseUrl="https://example.com/v1",Model="mock-reader"}],ReaderTopmost=false};
  public List<Request> Requests{get;}=[];public bool SlowSecond,SlowAlignment,Streaming,PartialPairs;public int Delay=12,TruncateAlignment;
  public TaskCompletionSource TextGate=new(TaskCreationOptions.RunContinuationsAsynchronously),AlignmentGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public int Count=>count;
  public void SaveSettings(){}public void ShowSettings(){}
  public Task<OcrResponse> RecognizeAsync(byte[] image,CancellationToken token)=>Task.FromResult(new OcrResponse(Enumerable.Range(1,count).Select(i=>new OcrLine($"Original {i}.",20,i*40,180,24,1)).ToList(),0));
  public TranslationService CreateTranslationService()=>new(new Handler(this));
 }
 sealed class Handler(FakeHost host):HttpMessageHandler
 {
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var messages=json.RootElement.GetProperty("messages");
   string system=messages[0].GetProperty("content").GetString()!;bool english=system.Contains("into English.");
   using var input=JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
   bool bilingual=input.RootElement.TryGetProperty("source",out _),align=input.RootElement.TryGetProperty("english",out _);
   var ids=bilingual||align?new[]{"b1"}:input.RootElement.GetProperty("blocks").EnumerateArray().Select(b=>b.GetProperty("id").GetString()!).ToArray();
   string source=align?input.RootElement.GetProperty("english").GetString()!:bilingual?input.RootElement.GetProperty("source").GetString()!:input.RootElement.GetProperty("blocks")[0].GetProperty("text").GetString()!;
   string? knownZh=align?input.RootElement.GetProperty("chinese").GetString():bilingual?input.RootElement.GetProperty("existingChinese").GetString():null;
   host.Requests.Add(new(align?"align":bilingual?"both":english?"en":"zh",ids,source,knownZh is not null));await Task.Delay((host.SlowSecond&&host.Requests.Count==2)||(host.SlowAlignment&&align)?400:host.Delay,ct);
   string en=string.Join(" ",Enumerable.Range(1,host.Count).Select(i=>$"EN b{i}. Read at a comfortable size. The whole paragraph should remain selectable and visible when you resize the window."));
   string zh=knownZh??string.Join(" ",Enumerable.Range(1,host.Count).Select(i=>$"中文 b{i}。调整字号后文字会自然换行。拖动窗口后保持位置，可以直接划选复制，末尾完整。"));
   bool truncated=align&&host.TruncateAlignment-->0;
   var content=truncated?"{\"pairs\":[[":align?JsonSerializer.Serialize(new{pairs=Enumerable.Range(1,host.Count).Select(i=>new[]{$"EN b{i}.",$"中文 b{i}。"})}):bilingual?JsonSerializer.Serialize(new{english=input.RootElement.GetProperty("existingEnglish").GetString()??en,chinese=zh}):JsonSerializer.Serialize(new{translations=ids.Select(id=>new{id,text=english?en:zh})});
   if(truncated&&host.PartialPairs)content="{\"pairs\":[[\"EN b1.\",\"中文 b1。\"],[";
   if(host.Streaming&&(bilingual||align))
   {
    int split=align?content.IndexOf("],",StringComparison.Ordinal)+1:content.IndexOf("\"chinese\"",StringComparison.Ordinal)+40;
    var prefix=Encoding.UTF8.GetBytes(StreamingTests.Delta(ApiProtocol.ChatCompletions,content[..split]));
    var suffix=Encoding.UTF8.GetBytes(StreamingTests.Delta(ApiProtocol.ChatCompletions,content[split..])+StreamingTests.Terminal(ApiProtocol.ChatCompletions));
    var stream=new StreamContent(new StreamingTests.GatedStream(prefix,suffix,(align?host.AlignmentGate:host.TextGate).Task));stream.Headers.ContentType=new("text/event-stream");
    return new(HttpStatusCode.OK){Content=stream};
   }
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{finish_reason=truncated?"length":"stop",message=new{content}}}}),Encoding.UTF8,"application/json")};
  }
 }
}

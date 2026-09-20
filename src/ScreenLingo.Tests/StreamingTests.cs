using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ScreenLingo.Core;

namespace ScreenLingo.Tests;

static class StreamingTests
{
 internal sealed class Immediate<T>(Action<T> action):IProgress<T> { public void Report(T value)=>action(value); }
 static readonly ApiProtocol[] Protocols=[ApiProtocol.ChatCompletions,ApiProtocol.Responses,ApiProtocol.Anthropic,ApiProtocol.Gemini];
 static ApiProfile Profile(ApiProtocol protocol)=>new(){BaseUrl="https://example.com/v1",Model="mock",Protocol=protocol};
 static TextRegion Selection=>new("b1",BilingualTests.English,0,0,800,60,24,1);
 public static async Task Run(Action<string,bool,object?> check)
 {
  foreach(var protocol in Protocols)
  {
   const string prefix="{\"sourceLanguage\":\"en\",\"chinese\":\"你好，";
   const string suffix="世界。再次你好。\"}";
   var handler=new Handler(protocol,prefix,suffix);using var api=new TranslationService(handler);
   var seen=new TaskCompletionSource<BilingualText>(TaskCreationOptions.RunContinuationsAsynchronously);
   var watch=Stopwatch.StartNew();
   var task=api.TranslateBilingualAsync(Profile(protocol),"",Selection,null,null,CancellationToken.None,new Immediate<BilingualText>(part=>{if(part.Chinese.Length>0)seen.TrySetResult(part);}));
   var preview=await seen.Task.WaitAsync(TimeSpan.FromSeconds(3));double first=watch.Elapsed.TotalMilliseconds;
   bool early=!task.IsCompleted&&preview.English==BilingualTests.English&&preview.Chinese=="你好，";
   handler.Release();var result=await task;
   check("streamed bilingual prefix before completion "+protocol,early&&handler.StreamRequested&&result.English==BilingualTests.English&&result.Chinese==BilingualTests.Chinese,new{firstPreviewMs=Math.Round(first,2),handler.Path});
   var alignmentHandler=new Handler(protocol,"{\"pairs\":[[\"world\",\"世界\"],[\"hello\",\"你好\",2",",2]]}");using var alignApi=new TranslationService(alignmentHandler);
   var links=new TaskCompletionSource<BilingualAlignment>(TaskCreationOptions.RunContinuationsAsynchronously);
   var aligning=alignApi.AlignBilingualAsync(Profile(protocol),"",BilingualTests.English,BilingualTests.Chinese,CancellationToken.None,new Immediate<BilingualAlignment>(a=>links.TrySetResult(a)));
   var firstLinks=await links.Task.WaitAsync(TimeSpan.FromSeconds(3));bool firstValid=firstLinks.Phrases.Count==1&&!aligning.IsCompleted;
   alignmentHandler.Release();var all=await aligning;
   check("only complete phrase pairs become available early "+protocol,firstValid&&all.Phrases.Count==2&&all.Match("en",14,5).SequenceEqual(new[]{new TextSpan(8,2)}),null);
   var truncatedHandler=new Handler(protocol,prefix,suffix){Truncated=true};truncatedHandler.Release();using var truncatedApi=new TranslationService(truncatedHandler);
   bool truncated=false;try{await truncatedApi.TranslateBilingualAsync(Profile(protocol),"",Selection,null,null,CancellationToken.None,new Immediate<BilingualText>(_=>{}));}catch(OutputTruncatedException){truncated=true;}
   check("streamed token limit is never treated as complete "+protocol,truncated,null);
  }
  var singleHandler=new Handler(ApiProtocol.ChatCompletions,"{\"translations\":[{\"id\":\"b1\",\"text\":\"\\u4F60\\u597D\\n\\\"hello\\\"\\uD83D", "\\uDE42结束\"}]}");
  using(var api=new TranslationService(singleHandler))
  {
   var previews=new List<string>();
   var task=api.TranslateAsync(Profile(ApiProtocol.ChatCompletions),"",[Selection],"zh",new Immediate<Dictionary<string,string>>(p=>previews.Add(p["b1"])),CancellationToken.None);
   await Task.Delay(60);singleHandler.Release();var result=await task;
   check("split Unicode escapes and quotes decode without leaking JSON",previews.Any(p=>p.Contains("你好\n\"hello\""))&&previews.All(p=>!p.Contains("\\u"))&&result["b1"]=="你好\n\"hello\"🙂结束",null);
  }
  var interrupted=new Handler(ApiProtocol.ChatCompletions,"{\"sourceLanguage\":\"en\",\"chinese\":\"你好\"}",""){NoTerminal=true};interrupted.Release();
  using(var api=new TranslationService(interrupted))
  {
   bool rejected=false;try{await api.TranslateBilingualAsync(Profile(ApiProtocol.ChatCompletions),"",Selection,null,null,CancellationToken.None,new Immediate<BilingualText>(_=>{}));}catch(IOException){rejected=true;}
   check("abrupt stream EOF rejects even syntactically complete JSON",rejected,null);
  }
  var cancelled=new Handler(ApiProtocol.ChatCompletions,"{\"sourceLanguage\":\"en\",\"chinese\":\"你好", "\"}");
  using(var api=new TranslationService(cancelled))using(var ct=new CancellationTokenSource())
  {
   var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var task=api.TranslateBilingualAsync(Profile(ApiProtocol.ChatCompletions),"",Selection,null,null,ct.Token,new Immediate<BilingualText>(_=>arrived.TrySetResult()));
   await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));ct.Cancel();bool stopped=false;try{await task;}catch(OperationCanceledException){stopped=true;}
   check("cancellation interrupts a stalled stream",stopped,null);
  }
  var disabled=new Handler(ApiProtocol.ChatCompletions,"",""){JsonOnly=true};using(var api=new TranslationService(disabled))
  {
   var profile=Profile(ApiProtocol.ChatCompletions);profile.StreamResponses=false;
   var result=await api.TranslateBilingualAsync(profile,"",Selection,null,null,CancellationToken.None,new Immediate<BilingualText>(_=>{}));
   check("streaming preference disables stream request and preserves JSON mode",!disabled.StreamRequested&&result.Chinese==BilingualTests.Chinese,null);
  }
  var compatible=new Handler(ApiProtocol.ChatCompletions,"",""){JsonOnly=true};using(var api=new TranslationService(compatible))
  {
   var result=await api.TranslateBilingualAsync(Profile(ApiProtocol.ChatCompletions),"",Selection,null,null,CancellationToken.None,new Immediate<BilingualText>(_=>{}));
   check("gateway returning ordinary JSON to a stream request remains compatible",compatible.StreamRequested&&result.English==BilingualTests.English,null);
  }
 }
 internal sealed class Handler(ApiProtocol protocol,string prefix,string suffix):HttpMessageHandler
 {
  readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public bool Truncated,NoTerminal,JsonOnly,StreamRequested;public string? Path;
  public void Release()=>release.TrySetResult();
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Path=request.RequestUri!.PathAndQuery;
   StreamRequested=protocol==ApiProtocol.Gemini?Path.Contains(":streamGenerateContent?alt=sse"):json.RootElement.GetProperty("stream").GetBoolean();
   if(JsonOnly)return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{finish_reason="stop",message=new{content=JsonSerializer.Serialize(new{sourceLanguage="en",chinese=BilingualTests.Chinese})}}}}),Encoding.UTF8,"application/json")};
   string first=": keepalive\r\n\r\n"+Reasoning(protocol)+Delta(protocol,prefix);
   string last=Delta(protocol,suffix)+(NoTerminal?"":Terminal(protocol,Truncated));
   var content=new StreamContent(new GatedStream(Encoding.UTF8.GetBytes(first),Encoding.UTF8.GetBytes(last),release.Task));
   content.Headers.ContentType=new("text/event-stream");return new(HttpStatusCode.OK){Content=content};
  }
 }
 static string Event(object item)=>"data: "+JsonSerializer.Serialize(item)+"\r\n\r\n";
 internal static string Delta(ApiProtocol protocol,string text)=>protocol switch
 {
  ApiProtocol.Responses=>Event(new{type="response.output_text.delta",delta=text}),
  ApiProtocol.Anthropic=>Event(new{type="content_block_delta",index=0,delta=new{type="text_delta",text}}),
  ApiProtocol.Gemini=>Event(new{candidates=new[]{new{content=new{parts=new[]{new{text}}}}}}),
  _=>Event(new{choices=new[]{new{index=0,delta=new{content=text}}}})
 };
 static string Reasoning(ApiProtocol protocol)=>protocol switch
 {
  ApiProtocol.Responses=>Event(new{type="response.reasoning_text.delta",delta="Hidden thought"}),
  ApiProtocol.Anthropic=>Event(new{type="content_block_delta",index=0,delta=new{type="thinking_delta",thinking="Hidden thought"}}),
  ApiProtocol.Gemini=>Event(new{candidates=new[]{new{content=new{parts=new[]{new{text="Hidden thought",thought=true}}}}}}),
  _=>Event(new{choices=new[]{new{index=0,delta=new{reasoning_content="Hidden thought"}}}})
 };
 internal static string Terminal(ApiProtocol protocol,bool truncated=false)=>protocol switch
 {
  ApiProtocol.Responses=>Event(new{type=truncated?"response.incomplete":"response.completed"}),
  ApiProtocol.Anthropic=>Event(new{type="message_delta",delta=new{stop_reason=truncated?"max_tokens":"end_turn"}})+Event(new{type="message_stop"}),
  ApiProtocol.Gemini=>Event(new{candidates=new[]{new{finishReason=truncated?"MAX_TOKENS":"STOP"}}}),
  _=>Event(new{choices=new[]{new{index=0,finish_reason=truncated?"length":"stop",delta=new{}}}})+"data: [DONE]\r\n\r\n"
 };
 internal sealed class GatedStream(byte[] prefix,byte[] suffix,Task release):Stream
 {
  int position;
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
  {
   if(position>=prefix.Length)await release.WaitAsync(ct).ConfigureAwait(false);
   var bytes=position<prefix.Length?prefix:suffix;int offset=position<prefix.Length?position:position-prefix.Length;
   int count=Math.Min(Math.Min(buffer.Length,7),bytes.Length-offset);if(count<=0)return 0;
   bytes.AsMemory(offset,count).CopyTo(buffer);position+=count;return count;
  }
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
  public override long Length=>prefix.Length+suffix.Length;public override long Position{get=>position;set=>throw new NotSupportedException();}
  public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
  public override void Flush(){}public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();
  public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
}

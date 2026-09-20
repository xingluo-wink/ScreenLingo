using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ScreenLingo.Core;

namespace ScreenLingo.Tests;

static class FastModeTests
{
 public static async Task Run(Action<string,bool,object?> check)
 {
  var saved=JsonSerializer.Deserialize<ApiProfile>("{\"Model\":\"deepseek-flash\"}")!;
  check("existing profiles get fast mode without rewriting credentials",saved.FastMode&&saved.Clone().FastMode,null);
  foreach(var (model,fast,extra,expected) in new[]{
   ("deepseek-flash",true,"{}","disabled"),("vendor/deepseek-v4-pro",true,"{}","disabled"),
   ("deepseek-flash",false,"{}",(string?)null),("different-model",true,"{}",(string?)null),
   ("deepseek-flash",true,"{\"thinking\":{\"type\":\"enabled\"}}","enabled"),
   ("deepseek-flash",true,"{\"reasoning_effort\":\"high\"}",(string?)null)})
  {
   var handler=new Handler();using var api=new TranslationService(handler);
   await api.TranslateAsync(new(){Model=model,FastMode=fast,ExtraBody=extra,BaseUrl="https://example.com/v1"},"",[new("b1","Hello",0,0,300,30,20,1)],"zh",null,CancellationToken.None);
   check("fast-mode compatibility "+model+" "+fast+" "+extra,handler.Thinking==expected&&handler.TokenLimit==4096,new{handler.Thinking});
  }
  var exhausted=new Handler{ReasoningOnly=true};using(var api=new TranslationService(exhausted))
  {
   bool explained=false;try{await api.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="deepseek-flash",FastMode=false},"",BilingualTests.English,BilingualTests.Chinese,CancellationToken.None);}catch(OutputTruncatedException ex){explained=ex.ReasoningOnly&&ex.Message.Contains("思考");}
   check("reasoning-only exhaustion reports the cause without a futile retry",explained&&exhausted.Calls==1,null);
  }
  foreach(var protocol in new[]{ApiProtocol.ChatCompletions,ApiProtocol.Responses,ApiProtocol.Anthropic,ApiProtocol.Gemini})
  {
   var handler=new StreamingTests.Handler(protocol,"{\"pairs\":[[\"world\",\"世界\"],[\"hello\"",""){Truncated=true};handler.Release();using var api=new TranslationService(handler);
   var result=await api.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test",Protocol=protocol},"",BilingualTests.English,BilingualTests.Chinese,CancellationToken.None,new StreamingTests.Immediate<BilingualAlignment>(_=>{}));
   check("truncated stream preserves complete phrase pairs "+protocol,result.IsPartial&&result.Phrases.Count==1&&result.Match("en",7,5).SequenceEqual(new[]{new TextSpan(3,2)}),null);
  }
  var buffered=new Handler{PartialPairs=true};using(var api=new TranslationService(buffered))
  {
   var result=await api.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test"},"",BilingualTests.English,BilingualTests.Chinese,CancellationToken.None);
   check("buffered truncation preserves useful pairs without another request",result.IsPartial&&result.Phrases.Count==1&&buffered.Calls==1,null);
  }
  var thinkingStream=new StreamingTests.Handler(ApiProtocol.ChatCompletions,"",""){Truncated=true};thinkingStream.Release();using(var api=new TranslationService(thinkingStream))
  {
   bool explained=false;try{await api.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test"},"",BilingualTests.English,BilingualTests.Chinese,CancellationToken.None,new StreamingTests.Immediate<BilingualAlignment>(_=>{}));}catch(OutputTruncatedException ex){explained=ex.ReasoningOnly;}
   check("streamed thinking-only exhaustion is distinguished from long output",explained,null);
  }
 }
 sealed class Handler:HttpMessageHandler
 {
  public bool ReasoningOnly,PartialPairs;public int Calls,TokenLimit;public string? Thinking;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   Calls++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var root=doc.RootElement;
   Thinking=root.TryGetProperty("thinking",out var thinking)?thinking.GetProperty("type").GetString():null;TokenLimit=root.GetProperty("max_tokens").GetInt32();
   string content=ReasoningOnly?"":PartialPairs?"{\"pairs\":[[\"world\",\"世界\"],[\"hello\"":"{\"translations\":[{\"id\":\"b1\",\"text\":\"你好\"}]}";
   var response=new{choices=new[]{new{finish_reason=ReasoningOnly||PartialPairs?"length":"stop",message=new{content,reasoning_content=ReasoningOnly?"A reasoning budget exhausted before the result.":null}}}};
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
  }
 }
}

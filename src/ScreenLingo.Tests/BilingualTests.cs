using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ScreenLingo.Core;
namespace ScreenLingo.Tests;

static class BilingualTests
{
 public const string English="hello, world. hello again.";
 public const string Chinese="你好，世界。再次你好。";
 public const string Links="""
 {"alignment":[{"en":"hello","zh":"你好","enOccurrence":1,"zhOccurrence":1},{"en":"world","zh":"世界"},{"en":"hello","zh":"你好","enOccurrence":2,"zhOccurrence":2},{"en":"again","zh":"再次"}]}
 """;
 public const string CompactLinks="""
 {"pairs":[["hello","你好",1,1],["world","世界"],["hello","你好",2,2],["again","再次"]]}
 """;
 public static BilingualAlignment Sample()
 {
  using var json=JsonDocument.Parse(Links);return BilingualAlignment.Parse(json.RootElement,English,Chinese);
 }
 public static void Core(Action<string,bool,object?> check)
 {
  var alignment=Sample();
  check("Chinese hello links to the first English hello",alignment.Match("zh",0,2).SequenceEqual(new[]{new TextSpan(0,5)}),null);
  check("second occurrence links in the reverse direction",alignment.Match("en",14,5).SequenceEqual(new[]{new TextSpan(8,2)}),null);
  check("different word order aligns by meaning",alignment.Match("zh",6,2).SequenceEqual(new[]{new TextSpan(20,5)}),null);
  check("selection spanning phrases merges the matching ranges",alignment.Match("en",14,11).SequenceEqual(new[]{new TextSpan(6,4)}),null);
  check("punctuation without a phrase link is not guessed",alignment.Match("zh",2,1).Count==0,null);
  using var bad=JsonDocument.Parse("""{"alignment":[{"en":"hello","zh":"你好"},{"en":"not present","zh":"世界"},{"en":"world","zh":"世界","enOccurrence":8},{"en":"world","zh":"世界","zhOccurrence":"1"},null]}""");
  check("ambiguous repetitions and invalid links are ignored",BilingualAlignment.Parse(bad.RootElement,English,Chinese).Phrases.Count==0,null);
  using var missing=JsonDocument.Parse("{}");
  check("missing alignment leaves the complete text available",BilingualAlignment.Parse(missing.RootElement,English,Chinese).Phrases.Count==0,null);
  using var unicode=JsonDocument.Parse("""{"alignment":[{"en":"hello","zh":"你好"}]}""");
  var emoji=BilingualAlignment.Parse(unicode.RootElement,"🙂 hello","🙂 你好");
  check("UTF-16 offsets remain correct after emoji",emoji.Match("zh",3,2).SequenceEqual(new[]{new TextSpan(3,5)}),null);
  using var words=JsonDocument.Parse("""{"alignment":[{"en":"he","zh":"他","enOccurrence":1}]}""");
  var boundary=BilingualAlignment.Parse(words.RootElement,"the person said he agreed","那个人说他同意了");
  check("short English words do not match inside other words",boundary.Match("zh",4,1).SequenceEqual(new[]{new TextSpan(16,2)}),null);
  using var compact=JsonDocument.Parse(CompactLinks);
  check("compact pairs retain exact occurrence alignment",BilingualAlignment.Parse(compact.RootElement,English,Chinese).Phrases.SequenceEqual(alignment.Phrases),null);
 }

 public static async Task Protocols(Action<string,bool,object?> check)
 {
  foreach(var protocol in Enum.GetValues<ApiProtocol>())
  {
   var handler=new Handler(protocol);using var service=new TranslationService(handler);
   var profile=new ApiProfile{BaseUrl="https://example.com/v1",Model="test-model",Protocol=protocol};
   var result=await service.TranslateBilingualAsync(profile,"test-only",new("b1",English,0,0,800,60,24,1),protocol==ApiProtocol.QwenMt?null:English,null,CancellationToken.None);
   var alignment=await service.AlignBilingualAsync(profile,"test-only",result.English,result.Chinese,CancellationToken.None);
   check("bilingual protocol "+protocol,result.English==English&&result.Chinese==Chinese&&handler.Valid&&handler.Calls==2&&alignment.Phrases.Count==(protocol==ApiProtocol.QwenMt?0:4),new{handler.Calls});
  }
  using var noLinks=new TranslationService(new Handler(ApiProtocol.ChatCompletions,true));
  var fallback=await noLinks.TranslateBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test-model"},"",new("b1",English,0,0,800,60,24,1),English,null,CancellationToken.None);
  var absent=await noLinks.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test-model"},"",fallback.English,fallback.Chinese,CancellationToken.None);
  check("missing phrase metadata does not discard translation",fallback.Chinese==Chinese&&absent.Phrases.Count==0,null);
  foreach(var protocol in new[]{ApiProtocol.ChatCompletions,ApiProtocol.Responses,ApiProtocol.Anthropic,ApiProtocol.Gemini})
  {
   var retry=new Handler(protocol){TruncateAlignment=1};using var service=new TranslationService(retry);
   var profile=new ApiProfile{BaseUrl="https://example.com/v1",Model="test-model",Protocol=protocol,MaxOutputTokens=4096};
   var aligned=await service.AlignBilingualAsync(profile,"",English,Chinese,CancellationToken.None);
   check("bounded alignment retry after truncation "+protocol,aligned.Phrases.Count==4&&retry.Calls==2&&retry.TextCalls==0&&retry.ObservedLimits.All(n=>n==4096)&&retry.PairLimits.SequenceEqual(new[]{80,40}),new{retry.Calls});
  }
  var exhausted=new Handler(ApiProtocol.ChatCompletions){TruncateAlignment=99};using var exhaustedService=new TranslationService(exhausted);
  bool stopped=false;try{await exhaustedService.AlignBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test-model"},"",English,Chinese,CancellationToken.None);}catch(OutputTruncatedException){stopped=true;}
  check("alignment retries stop after the second truncation",stopped&&exhausted.Calls==2,null);
 }

 sealed class Handler(ApiProtocol protocol,bool noLinks=false):HttpMessageHandler
 {
  public int Calls,TextCalls,TruncateAlignment;public bool Valid=true;
  public List<int> ObservedLimits=[];public List<int> PairLimits=[];
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   Calls++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var root=body.RootElement;
   string input=protocol switch
   {
    ApiProtocol.Responses=>root.GetProperty("input").GetString()!,
    ApiProtocol.Gemini=>root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!,
    ApiProtocol.Anthropic or ApiProtocol.QwenMt=>root.GetProperty("messages")[0].GetProperty("content").GetString()!,
    _=>root.GetProperty("messages")[1].GetProperty("content").GetString()!
   };
   string content;bool truncated=false;
   if(protocol==ApiProtocol.QwenMt)
   {
    Valid&=input==English;content=root.GetProperty("translation_options").GetProperty("target_lang").GetString()=="English"?English:Chinese;
   }
   else
   {
    using var data=JsonDocument.Parse(input);
    if(data.RootElement.TryGetProperty("source",out var source))
    {
     TextCalls++;Valid&=source.GetString()==English&&data.RootElement.GetProperty("existingEnglish").GetString()==English;
     content=JsonSerializer.Serialize(new{english="Do not overwrite the cached text with this!",chinese=Chinese});
    }
    else
    {
     Valid&=data.RootElement.GetProperty("english").GetString()==English&&data.RootElement.GetProperty("chinese").GetString()==Chinese;
     int limit=protocol switch{ApiProtocol.Responses=>root.GetProperty("max_output_tokens").GetInt32(),ApiProtocol.Gemini=>root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32(),_=>root.GetProperty("max_tokens").GetInt32()};ObservedLimits.Add(limit);
     string system=protocol switch{ApiProtocol.Responses=>root.GetProperty("instructions").GetString()!,ApiProtocol.Gemini=>root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!,ApiProtocol.Anthropic=>root.GetProperty("system").GetString()!,_=>root.GetProperty("messages")[0].GetProperty("content").GetString()!};
     PairLimits.Add(int.Parse(System.Text.RegularExpressions.Regex.Match(system,@"at most (\d+) pairs").Groups[1].Value));
     truncated=TruncateAlignment-- >0;content=truncated?"{\"pairs\":[[\"hello\"":noLinks?"{\"pairs\":[]}":CompactLinks;
    }
   }
   object response=protocol switch
   {
    ApiProtocol.Responses=>new{status=truncated?"incomplete":"completed",output_text=content},
    ApiProtocol.Anthropic=>new{stop_reason=truncated?"max_tokens":"end_turn",content=new[]{new{type="text",text=content}}},
    ApiProtocol.Gemini=>new{candidates=new[]{new{finishReason=truncated?"MAX_TOKENS":"STOP",content=new{parts=new[]{new{text=content}}}}}},
    _=>new{choices=new[]{new{finish_reason=truncated?"length":"stop",message=new{content}}}}
   };
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
  }
 }
}

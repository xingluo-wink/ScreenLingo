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
 }

 public static async Task Protocols(Action<string,bool,object?> check)
 {
  foreach(var protocol in Enum.GetValues<ApiProtocol>())
  {
   var handler=new Handler(protocol);using var service=new TranslationService(handler);
   var profile=new ApiProfile{BaseUrl="https://example.com/v1",Model="test-model",Protocol=protocol};
   var result=await service.TranslateBilingualAsync(profile,"test-only",new("b1",English,0,0,800,60,24,1),protocol==ApiProtocol.QwenMt?null:English,null,CancellationToken.None);
   check("bilingual protocol "+protocol,result.English==English&&result.Chinese==Chinese&&handler.Valid&&handler.Calls==(protocol==ApiProtocol.QwenMt?2:1)&&result.Alignment.Phrases.Count==(protocol==ApiProtocol.QwenMt?0:4),new{handler.Calls});
  }
  using var noLinks=new TranslationService(new Handler(ApiProtocol.ChatCompletions,true));
  var fallback=await noLinks.TranslateBilingualAsync(new(){BaseUrl="https://example.com/v1",Model="test-model"},"",new("b1",English,0,0,800,60,24,1),English,null,CancellationToken.None);
  check("missing phrase metadata does not discard translation",fallback.Chinese==Chinese&&fallback.Alignment.Phrases.Count==0,null);
 }

 sealed class Handler(ApiProtocol protocol,bool noLinks=false):HttpMessageHandler
 {
  public int Calls;public bool Valid=true;
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
   string content;
   if(protocol==ApiProtocol.QwenMt)
   {
    Valid&=input==English;content=root.GetProperty("translation_options").GetProperty("target_lang").GetString()=="English"?English:Chinese;
   }
   else
   {
    using var data=JsonDocument.Parse(input);Valid&=data.RootElement.GetProperty("source").GetString()==English&&data.RootElement.GetProperty("existingEnglish").GetString()==English;
    using var links=JsonDocument.Parse(Links);
    content=JsonSerializer.Serialize(new{english="Do not overwrite the cached text with this!",chinese=Chinese,alignment=noLinks?JsonSerializer.SerializeToElement(Array.Empty<object>()):links.RootElement.GetProperty("alignment")});
   }
   object response=protocol switch
   {
    ApiProtocol.Responses=>new{status="completed",output_text=content},
    ApiProtocol.Anthropic=>new{content=new[]{new{type="text",text=content}}},
    ApiProtocol.Gemini=>new{candidates=new[]{new{content=new{parts=new[]{new{text=content}}}}}},
    _=>new{choices=new[]{new{finish_reason="stop",message=new{content}}}}
   };
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
  }
 }
}

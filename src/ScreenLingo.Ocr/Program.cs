using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using RapidOcrNet;
using ScreenLingo.Core;
using SkiaSharp;
namespace ScreenLingo.Ocr;
internal static class Program
{
 static async Task<int> Main(string[] args)
 {
  try
  {
   var models = Path.Combine(AppContext.BaseDirectory, "models");
   using var engine = new RapidOcr();
   using var options = RapidOcr.GetDefaultSessionOptions(2);
   options.InterOpNumThreads = 1;
   // Screenshots change shape frequently. Retaining ONNX arenas otherwise keeps
   // the largest temporary tensors resident even after inference has finished.
   options.EnableCpuMemArena = false;
   options.EnableMemoryPattern = false;
   options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
   options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
   var load = Stopwatch.StartNew();
   engine.InitModels(Path.Combine(models,"det.onnx"),Path.Combine(models,"cls.onnx"),Path.Combine(models,"rec.onnx"),Path.Combine(models,"dict.txt"),options);
   load.Stop();
   if (args.Length == 2 && args[0] == "--image")
   {
    var result = Recognize(engine,new OcrRequest(await File.ReadAllBytesAsync(args[1])));
    Console.WriteLine(JsonSerializer.Serialize(new { loadMs=load.Elapsed.TotalMilliseconds,result }));
    return result.Error is null ? 0 : 1;
   }
   if (args.Length != 2 || args[0] != "--pipe") return 2;
   using var pipe = new NamedPipeServerStream(args[1],PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
   using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   await pipe.WaitForConnectionAsync(connectTimeout.Token);
   while (pipe.IsConnected)
   {
    OcrRequest request;
    try { request = await PipeWire.ReadAsync<OcrRequest>(pipe,CancellationToken.None); }
    catch (EndOfStreamException) { break; }
    OcrResponse response;
    try { response = Recognize(engine,request); }
    catch (Exception) { response = new([],0,"本地文字识别失败，请缩小选区后重试。"); }
    await PipeWire.WriteAsync(pipe,response,CancellationToken.None);
    // Temporary tensor arrays are large; collect after a user-initiated capture
    // instead of retaining many captures until the workstation-wide threshold.
    GC.Collect(2,GCCollectionMode.Forced,false);
   }
   return 0;
  }
  catch (Exception ex) { Console.Error.WriteLine($"OCR worker: {ex.GetType().Name}: {ex.Message}"); return 1; }
 }
 static OcrResponse Recognize(RapidOcr engine,OcrRequest request)
 {
  using var bitmap = SKBitmap.Decode(request.Png) ?? throw new InvalidDataException("Invalid image");
  if ((long)bitmap.Width*bitmap.Height > 40_000_000) return new([],0,"选区超过 4000 万像素，请缩小范围。");
  var clock = Stopwatch.StartNew();
  var result = engine.Detect(bitmap,RapidOcrOptions.Default with { DoAngle=false,Padding=24,
   ImgResize=Math.Clamp(Math.Max(bitmap.Width,bitmap.Height)+48,960,request.MaxSide),TextScore=.35f,BoxScoreThresh=.5f });
  var lines = result.TextBlocks.Select(b => {
   var x=Math.Clamp(b.BoxPoints.Min(p=>p.X),0,bitmap.Width); var y=Math.Clamp(b.BoxPoints.Min(p=>p.Y),0,bitmap.Height);
   var right=Math.Clamp(b.BoxPoints.Max(p=>p.X),0,bitmap.Width); var bottom=Math.Clamp(b.BoxPoints.Max(p=>p.Y),0,bitmap.Height);
   return new OcrLine(b.Text,x,y,right-x,bottom-y,b.CharScores?.Average() ?? 0);
  }).Where(l=>l.Width>1 && l.Height>1).ToList();
  return new(lines,clock.Elapsed.TotalMilliseconds);
 }
}

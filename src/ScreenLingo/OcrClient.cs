using System.Diagnostics;
using System.IO.Pipes;
using ScreenLingo.Core;
namespace ScreenLingo;
public sealed class OcrClient:IDisposable
{
 readonly SemaphoreSlim gate=new(1,1);Process? process;NamedPipeClientStream? pipe;DateTime lastUsed=DateTime.UtcNow;
 readonly System.Threading.Timer idle;readonly string workerDirectory;
 public int IdleSeconds{get;set;}=120;
 public bool IsLoaded=>process is {HasExited:false};
 public int? WorkerPid=>IsLoaded?process!.Id:null;
 public OcrClient(string? workerDirectory=null){this.workerDirectory=workerDirectory??AppContext.BaseDirectory;idle=new(_=>ReleaseIfIdle(),null,10000,10000);}
 public async Task<OcrResponse> RecognizeAsync(byte[] png,CancellationToken token)
 {
  await gate.WaitAsync(token);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(90));
  try
  {
   if(!IsLoaded||pipe is not {IsConnected:true})
   {
    StopCore();string name="ScreenLingo-"+Guid.NewGuid().ToString("N");string executable=Path.Combine(workerDirectory,"ScreenLingo.Ocr.exe");
    if(!File.Exists(executable))throw new FileNotFoundException("缺少 OCR 组件，请完整解压软件目录。",executable);
    var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=workerDirectory};start.ArgumentList.Add("--pipe");start.ArgumentList.Add(name);
    process=Process.Start(start)??throw new IOException("无法启动本地 OCR。");pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
    using var connection=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);connection.CancelAfter(TimeSpan.FromSeconds(30));await pipe.ConnectAsync(connection.Token);
   }
   await PipeWire.WriteAsync(pipe!,new OcrRequest(png),deadline.Token);var result=await PipeWire.ReadAsync<OcrResponse>(pipe!,deadline.Token);
   if(result.Error is not null)throw new InvalidDataException(result.Error);lastUsed=DateTime.UtcNow;return result;
  }
  catch(OperationCanceledException)when(!token.IsCancellationRequested){StopCore();throw new TimeoutException("OCR 启动或识别超时。请确认模型文件完整，然后缩小选区重试。");}
  catch{StopCore();throw;}
  finally{gate.Release();}
 }
 void ReleaseIfIdle(){if(!gate.Wait(0))return;try{if((DateTime.UtcNow-lastUsed).TotalSeconds>=IdleSeconds)StopCore();}finally{gate.Release();}}
 public void ReleaseNow(){if(!gate.Wait(0))return;try{StopCore();}finally{gate.Release();}}
 void StopCore(){pipe?.Dispose();pipe=null;if(process is not null){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}process.Dispose();process=null;}}
 public void Dispose(){idle.Dispose();ReleaseNow();}
}

using System.Diagnostics;
using System.Windows.Media.Imaging;
using ScreenLingo.Core;
using Forms=System.Windows.Forms;
namespace ScreenLingo;
internal static class Program
{
 [STAThread]public static void Main(string[] args)
 {
  string? data=null;int position=Array.IndexOf(args,"--data-dir");if(position>=0&&position+1<args.Length)data=Path.GetFullPath(args[position+1]);
  try{new ScreenApp(data,args).Run();}
  catch(Exception ex){MessageBox.Show("屏译启动失败：\n"+ex.Message,"ScreenLingo",MessageBoxButton.OK,MessageBoxImage.Error);}
 }
}
public sealed class ScreenApp:Application,IReaderHost
{
 public SettingsStore Store{get;}public AppSettings Settings{get;}public OcrClient Ocr{get;}=new();public bool Quitting{get;private set;}
 Native.Hotkey? hotkey;Forms.NotifyIcon? tray;MainWindow? settingsWindow;OverlayWindow? overlay;SelectionWindow? selector;bool capturing;
 readonly Mutex instance;readonly EventWaitHandle wake;RegisteredWaitHandle? wakeRegistration;bool ownsInstance;readonly string[] arguments;
 public ScreenApp(string? data,string[] args)
 {
  arguments=args;Store=new(data);Settings=Store.Load();Ocr.IdleSeconds=Settings.IdleSeconds;
  string identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Store.DirectoryPath+Environment.UserName)))[..20];
  instance=new Mutex(true,"Local\\ScreenLingo-"+identity,out ownsInstance);wake=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ScreenLingo-Wake-"+identity);
  ShutdownMode=ShutdownMode.OnExplicitShutdown;
  DispatcherUnhandledException+=(_,e)=>{e.Handled=true;MessageBox.Show("操作未完成：\n"+e.Exception.Message,"屏译",MessageBoxButton.OK,MessageBoxImage.Information);};
 }
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);if(!ownsInstance){wake.Set();Shutdown();return;}
  Ui.Install(this);wakeRegistration=ThreadPool.RegisterWaitForSingleObject(wake,(_,_)=>Dispatcher.BeginInvoke(ShowSettings),null,Timeout.Infinite,false);
  hotkey=new Native.Hotkey(BeginCapture);
  try{hotkey.Set(Settings.Hotkey);}catch(Exception ex){MessageBox.Show(ex.Message+"\n可在设置中修改，或点击「框选屏幕」。","屏译");}
  var menu=new Forms.ContextMenuStrip();menu.Items.Add("框选翻译  "+Settings.Hotkey,null,(_,_)=>Dispatcher.BeginInvoke(BeginCapture));menu.Items.Add("打开设置",null,(_,_)=>Dispatcher.BeginInvoke(ShowSettings));menu.Items.Add("释放 OCR 内存",null,(_,_)=>Ocr.ReleaseNow());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出屏译",null,(_,_)=>Dispatcher.BeginInvoke(Quit));
  tray=new Forms.NotifyIcon{Text="屏译 · "+Settings.Hotkey,Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowSettings);
  int imageIndex=Array.IndexOf(arguments,"--image");
  if(imageIndex>=0&&imageIndex+1<arguments.Length)OpenImage(arguments[imageIndex+1]);else ShowSettings();
 }
 public void ShowSettings()
 {
  if(settingsWindow is null){settingsWindow=new MainWindow(this);settingsWindow.Closed+=(sender,_)=>{if(ReferenceEquals(MainWindow,sender))MainWindow=null;settingsWindow=null;ScheduleCollection();};}
  settingsWindow.Owner=overlay?.IsVisible==true?overlay:null;settingsWindow.Topmost=settingsWindow.Owner?.Topmost==true;
  settingsWindow.Show();settingsWindow.WindowState=WindowState.Normal;settingsWindow.Activate();
 }
 void ScheduleCollection()=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>GC.Collect(2,GCCollectionMode.Forced,false)));
 public void SaveSettings()=>Store.Save(Settings);
 public Task<OcrResponse> RecognizeAsync(byte[] image,CancellationToken token)=>Ocr.RecognizeAsync(image,token);
 public TranslationService CreateTranslationService()=>new();
 public void ChangeHotkey(string value){hotkey?.Set(value);if(tray is not null)tray.Text="屏译 · "+value;}
 public async void BeginCapture()
 {
  if(capturing||selector is not null)return;capturing=true;
  try
  {
   overlay?.Close();overlay=null;settingsWindow?.Close();await Task.Delay(140);if(Quitting)return;
   selector=new SelectionWindow();selector.ShowDialog();var result=selector.Result;selector=null;
   if(result is not null)ShowOverlay(result);
  }
  catch(Exception ex){selector?.Close();selector=null;MessageBox.Show(ex.Message,"无法截取屏幕");ShowSettings();}
  finally{capturing=false;}
 }
 public void OpenImage(string path)
 {
  try
  {
   var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();
   if((long)image.PixelWidth*image.PixelHeight>40_000_000)throw new InvalidOperationException("图片过大，请选用较小图片。");
   var area=Forms.Screen.PrimaryScreen!.WorkingArea;
   int w=Math.Min(image.PixelWidth,area.Width-80),h=Math.Min(image.PixelHeight,area.Height-160);
   ShowOverlay(new(image,new(area.X+(area.Width-w)/2,area.Y+100,w,h)));
  }
  catch(Exception ex){MessageBox.Show(ex.Message,"无法打开图片");}
 }
 void ShowOverlay(CaptureResult result){overlay=new OverlayWindow(this,result);overlay.Closed+=(sender,_)=>{if(ReferenceEquals(MainWindow,sender))MainWindow=null;overlay=null;ScheduleCollection();};overlay.Show();}
 public void Quit(){Quitting=true;overlay?.Close();selector?.Close();settingsWindow?.Close();Shutdown();}
 protected override void OnExit(ExitEventArgs e)
 {
  wakeRegistration?.Unregister(null);wake.Dispose();hotkey?.Dispose();if(tray is not null){tray.Visible=false;tray.Dispose();}Ocr.Dispose();
  if(ownsInstance)instance.ReleaseMutex();instance.Dispose();base.OnExit(e);
 }
}

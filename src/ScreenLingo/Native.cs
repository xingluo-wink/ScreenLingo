using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
namespace ScreenLingo;
public static class Native
{
 [StructLayout(LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowRect(IntPtr hwnd,out WindowRect rect);
 [StructLayout(LayoutKind.Sequential)]public struct PixelPoint{public int X,Y;}
 [DllImport("user32.dll")]public static extern bool GetCursorPos(out PixelPoint point);
 [DllImport("user32.dll")]public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
 public static void Place(Window window,int x,int y,int width,int height)=>SetWindowPos(new WindowInteropHelper(window).Handle,new IntPtr(-1),x,y,width,height,0x0010);
 public static void PlaceReader(Window window,System.Drawing.Rectangle bounds)=>SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0014);
 public static System.Drawing.Rectangle ReaderBounds(Window window)
 {
  if(!GetWindowRect(new WindowInteropHelper(window).Handle,out var r))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  return System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
 }
 public static System.Drawing.Rectangle DesktopBounds=>new(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));
 public static (uint Modifiers,uint Key) ParseHotkey(string value)
 {
  var parts=value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);uint modifiers=0;Key key=Key.None;
  foreach(var part in parts)
  {
   if(part.Equals("Ctrl",StringComparison.OrdinalIgnoreCase))modifiers|=2;
   else if(part.Equals("Alt",StringComparison.OrdinalIgnoreCase))modifiers|=1;
   else if(part.Equals("Shift",StringComparison.OrdinalIgnoreCase))modifiers|=4;
   else if(!Enum.TryParse(part,true,out key))throw new ArgumentException("快捷键示例：Ctrl+Alt+Q。");
  }
  if(modifiers==0||key is Key.None or Key.F12 or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
   throw new ArgumentException("请使用 Ctrl / Alt / Shift 加字母或功能键，避开 F12。");
  return(modifiers|0x4000,(uint)KeyInterop.VirtualKeyFromKey(key));
 }
 public sealed class Hotkey:IDisposable
 {
  readonly HwndSource source;readonly Action triggered;int registered;
  public Hotkey(Action action){triggered=action;source=new HwndSource(new HwndSourceParameters("ScreenLingo.Hotkey"){ParentWindow=new IntPtr(-3),Width=0,Height=0,WindowStyle=0});source.AddHook(Hook);}
  public void Set(string value)
  {
   var(mod,key)=ParseHotkey(value);int next=registered==1?2:1;
   if(!RegisterHotKey(source.Handle,next,mod,key))throw new InvalidOperationException("这个快捷键已被其他软件占用，请换一个组合。");
   if(registered!=0)UnregisterHotKey(source.Handle,registered);registered=next;
  }
  IntPtr Hook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled){if(message==0x0312){handled=true;triggered();}return IntPtr.Zero;}
  public void Dispose(){if(registered!=0)UnregisterHotKey(source.Handle,registered);source.Dispose();}
 }
}

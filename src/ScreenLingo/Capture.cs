using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
namespace ScreenLingo;
public sealed record CaptureResult(BitmapSource Image,System.Drawing.Rectangle Bounds);
public static class Capture
{
 public static BitmapSource Desktop(System.Drawing.Rectangle area)
 {
  if(area.Width<=0||area.Height<=0||(long)area.Width*area.Height>80_000_000)throw new InvalidOperationException("屏幕尺寸不支持，请调整显示器布局。");
  using var bitmap=new System.Drawing.Bitmap(area.Width,area.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
  using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(area.X,area.Y,0,0,area.Size,System.Drawing.CopyPixelOperation.SourceCopy);
  using var memory=new MemoryStream();bitmap.Save(memory,ImageFormat.Png);memory.Position=0;
  var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.StreamSource=memory;source.EndInit();source.Freeze();return source;
 }
 public static byte[] Png(BitmapSource bitmap){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
}
public sealed class SelectionWindow:Window
{
 readonly System.Drawing.Rectangle desktop;BitmapSource? screenshot;readonly SelectionSurface surface;
 Native.PixelPoint start;System.Drawing.Rectangle selection;bool dragging;
 public CaptureResult? Result{get;private set;}
 public SelectionWindow()
 {
  desktop=Native.DesktopBounds;screenshot=Capture.Desktop(desktop);
  Title="屏译 · 框选";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=true;Topmost=true;Background=Brushes.Black;Cursor=Cursors.Cross;
  surface=new SelectionSurface(screenshot){Focusable=true};Content=surface;
  Loaded+=(_,_)=>{Activate();surface.Focus();};
  SourceInitialized+=(_,_)=>Native.Place(this,desktop.X,desktop.Y,desktop.Width,desktop.Height);
  PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
  MouseLeftButtonDown+=(_,e)=>{start=EventPosition(e);dragging=true;CaptureMouse();UpdateSelection(e);e.Handled=true;};
  MouseMove+=(_,e)=>{if(dragging)UpdateSelection(e);};
  MouseLeftButtonUp+=(_,e)=>
  {
   if(!dragging)return;UpdateSelection(e);dragging=false;ReleaseMouseCapture();
   if(selection.Width<8||selection.Height<8){surface.Selection=Rect.Empty;surface.InvalidateVisual();return;}
   var crop=new CroppedBitmap(screenshot!,new Int32Rect(selection.X-desktop.X,selection.Y-desktop.Y,selection.Width,selection.Height));crop.Freeze();
   // Detach the small crop from the full desktop backing buffer.
   using var memory=new MemoryStream(Capture.Png(crop));var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=memory;image.EndInit();image.Freeze();
   Result=new(image,selection);Close();e.Handled=true;
  };
  Closed+=(_,_)=>{surface.Clear();screenshot=null;Content=null;};
 }
 Native.PixelPoint EventPosition(MouseEventArgs e)
 {
  var p=e.GetPosition(surface);
  return new(){X=desktop.Left+(int)Math.Round(p.X*desktop.Width/surface.ActualWidth),Y=desktop.Top+(int)Math.Round(p.Y*desktop.Height/surface.ActualHeight)};
 }
 void UpdateSelection(MouseEventArgs e)
 {
  var current=EventPosition(e);int x1=Math.Clamp(Math.Min(start.X,current.X),desktop.Left,desktop.Right),y1=Math.Clamp(Math.Min(start.Y,current.Y),desktop.Top,desktop.Bottom);
  int x2=Math.Clamp(Math.Max(start.X,current.X),desktop.Left,desktop.Right),y2=Math.Clamp(Math.Max(start.Y,current.Y),desktop.Top,desktop.Bottom);
  selection=new(x1,y1,x2-x1,y2-y1);surface.Selection=new Rect(x1-desktop.X,y1-desktop.Y,x2-x1,y2-y1);surface.InvalidateVisual();
 }
 sealed class SelectionSurface(BitmapSource image):FrameworkElement
 {
  BitmapSource? image=image;public Rect Selection{get;set;}=Rect.Empty;
  public void Clear(){image=null;InvalidateVisual();}
  protected override void OnRender(DrawingContext dc)
  {
   if(image is null||ActualWidth<=0)return;
   dc.DrawImage(image,new Rect(0,0,ActualWidth,ActualHeight));
   double sx=ActualWidth/image.PixelWidth,sy=ActualHeight/image.PixelHeight;
   var screen=new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight));
   var selected=Selection.IsEmpty?Rect.Empty:new Rect(Selection.X*sx,Selection.Y*sy,Selection.Width*sx,Selection.Height*sy);
   Geometry shade=selected.IsEmpty?screen:new CombinedGeometry(GeometryCombineMode.Exclude,screen,new RectangleGeometry(selected));
   dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(115,0,0,0)),null,shade);
   if(!selected.IsEmpty){dc.DrawRectangle(null,new Pen(Ui.Accent,2),selected);}
   string hint=selected.IsEmpty?"拖动框选要翻译的内容  ·  Esc 取消":$"{Selection.Width:0} × {Selection.Height:0}  ·  松开鼠标完成";
   var text=new FormattedText(hint,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),15,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
   double x=selected.IsEmpty?36:Math.Clamp(selected.Left,8,Math.Max(8,ActualWidth-text.Width-28));double y=selected.IsEmpty?36:Math.Clamp(selected.Bottom+10,8,Math.Max(8,ActualHeight-46));
   dc.DrawRoundedRectangle(Ui.Ink,null,new Rect(x,y,text.Width+24,36),8,8);dc.DrawText(text,new Point(x+12,y+7));
  }
 }
}

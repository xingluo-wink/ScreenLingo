using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenLingo.Core;
using Forms=System.Windows.Forms;
namespace ScreenLingo;
public sealed class OverlayWindow:Window
{
 readonly ScreenApp app;readonly CaptureResult capture;readonly TranslationCanvas canvas;readonly Grid stage=new();readonly Viewbox view;readonly ScrollViewer reader;
 readonly StackPanel paragraphs=new();readonly Window toolbar;readonly TextBlock status=Ui.Text("正在识别选区…",12,Ui.Muted);
 readonly CancellationTokenSource lifetime=new();CancellationTokenSource? translating;Task? recognition;
 List<TextRegion> regions=[];Dictionary<string,string> translations=[];readonly Dictionary<string,Dictionary<string,string>> cache=[];
 bool original,reading,holding,closed;int generation;string target="";System.Drawing.Rectangle displayBounds;
 public OverlayWindow(ScreenApp application,CaptureResult result)
 {
  app=application;capture=result;displayBounds=result.Bounds;Title="屏译 · 译文";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=true;Background=Brushes.White;
  canvas=new(result.Image){Focusable=true};view=new Viewbox{Child=canvas,Stretch=Stretch.Fill};stage.Children.Add(view);
  reader=new ScrollViewer{Content=paragraphs,Padding=new Thickness(14),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Ui.Background,Visibility=Visibility.Collapsed};stage.Children.Add(reader);Content=stage;
  Activated+=(_,_)=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>Keyboard.Focus(reading?reader:canvas)));
  canvas.MouseDown+=(_,_)=>Keyboard.Focus(canvas);
  toolbar=BuildToolbar();
  SourceInitialized+=(_,_)=>Native.Place(this,result.Bounds.X,result.Bounds.Y,result.Bounds.Width,result.Bounds.Height);
  Loaded+=async(_,_)=>{toolbar.Owner=this;toolbar.Show();PlaceToolbar();Activate();canvas.Focus();recognition=RecognizeAsync();await recognition;};
  SizeChanged+=(_,_)=>PlaceToolbar();
  WireKeys(this);WireKeys(toolbar);
  Closed+=(_,_)=>{closed=true;lifetime.Cancel();translating?.Cancel();toolbar.Close();canvas.Clear();view.Child=null;paragraphs.Children.Clear();Content=null;cache.Clear();};
 }
 Window BuildToolbar()
 {
  var window=new Window{Title="屏译 · 工具条",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,SizeToContent=SizeToContent.WidthAndHeight,Background=Brushes.White};
  var panel=new StackPanel();var actions=new WrapPanel();
  actions.Children.Add(Ui.Btn("中文",()=>_=TranslateAsync("zh"),true));actions.Children.Add(Ui.Btn("English",()=>_=TranslateAsync("en"),true));
  actions.Children.Add(Ui.Btn("原图",()=>{original=!original;UpdateMode();}));actions.Children.Add(Ui.Btn("原位 / 阅读",()=>{reading=!reading;original=false;BuildReader();UpdateMode();}));
  actions.Children.Add(Ui.Btn("复制",ShowCopyMenu));actions.Children.Add(Ui.Btn("保存",ShowSaveMenu));actions.Children.Add(Ui.Btn("设置",app.ShowSettings));actions.Children.Add(Ui.Btn("关闭 ×",Close));panel.Children.Add(actions);
  status.Margin=new Thickness(2,8,0,0);status.MaxWidth=720;panel.Children.Add(status);window.Content=Ui.Card(panel,new Thickness(12));return window;
 }
 void PlaceToolbar()
 {
  if(!toolbar.IsVisible)return;var screen=Forms.Screen.FromRectangle(capture.Bounds).WorkingArea;var dpi=VisualTreeHelper.GetDpi(toolbar);
  int w=(int)Math.Ceiling(toolbar.ActualWidth*dpi.DpiScaleX),h=(int)Math.Ceiling(toolbar.ActualHeight*dpi.DpiScaleY);
  int x=Math.Clamp(displayBounds.Left,screen.Left,Math.Max(screen.Left,screen.Right-w));int y=displayBounds.Top-h-8;
  if(y<screen.Top)y=displayBounds.Bottom+8;if(y+h>screen.Bottom)y=screen.Top;
  Native.Place(toolbar,x,y,Math.Min(w,screen.Width),h);
 }
 void WireKeys(Window window)
 {
  window.PreviewKeyDown+=(_,e)=>
  {
   var pressed=e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;
   if(pressed==Key.Escape){e.Handled=true;Close();}
   else if(pressed==Key.Space){holding=true;UpdateMode();e.Handled=true;}
   else if(pressed==Key.C && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;_=TranslateAsync("zh");}
   else if(pressed==Key.E && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;_=TranslateAsync("en");}
   else if(pressed==Key.R && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;reading=!reading;original=false;BuildReader();UpdateMode();}
   else if(pressed==Key.O && Keyboard.Modifiers==ModifierKeys.None){e.Handled=true;original=!original;UpdateMode();}
   else if(pressed==Key.S && Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;SaveImage(false);}
   else if(pressed==Key.S && Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift) && translations.Count>0){e.Handled=true;SaveImage(true);}
  };
  window.PreviewKeyUp+=(_,e)=>{if(e.Key==Key.Space){holding=false;UpdateMode();e.Handled=true;}};
  window.Deactivated+=(_,_)=>{if(holding){holding=false;UpdateMode();}};
 }
 async Task RecognizeAsync()
 {
  try
  {
   var watch=Stopwatch.StartNew();var response=await app.Ocr.RecognizeAsync(Capture.Png(capture.Image),lifetime.Token);if(closed)return;
   regions=LayoutGrouper.Group(response.Lines);canvas.Regions=regions;BuildReader();
   status.Text=regions.Count==0?"未识别到文字。可保存原图，或退出后重新框选。":$"识别 {regions.Count} 个文字块 · {watch.Elapsed.TotalSeconds:F1} 秒 · 选择中文 / English · 按住空格看原图";
  }
  catch(OperationCanceledException){}
  catch(Exception ex){if(!closed)status.Text=ex.Message;}
  PlaceToolbar();
 }
 async Task TranslateAsync(string language)
 {
  int mine=++generation;translating?.Cancel();translating=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=translating.Token;
  try
  {
   if(recognition is not null)await recognition;token.ThrowIfCancellationRequested();if(regions.Count==0){status.Text="没有可翻译的文字，请重新框选。";return;}
   var profile=app.Settings.ActiveProfile?.Clone()??throw new ArgumentException("请先在设置中配置 API。");TranslationService.ValidateProfile(profile);
   string fingerprint=System.Text.Json.JsonSerializer.Serialize(profile)+language;
   target=language;original=false;holding=false;translations=[];canvas.Translations=translations;UpdateMode();
   if(cache.TryGetValue(fingerprint,out var cached)){translations=new(cached);ApplyResult(true);status.Text="已切换缓存译文 · 按住空格看原图";return;}
   status.Text="正在翻译为"+(language=="zh"?"中文":"英文")+"… 可切换语言或按 Esc 取消";PlaceToolbar();
   var watch=Stopwatch.StartNew();using var api=new TranslationService();
   var progress=new Progress<Dictionary<string,string>>(p=>{if(closed||mine!=generation)return;translations=p;ApplyResult(false);status.Text=$"正在翻译… {p.Count} / {regions.Count}";});
   var result=await api.TranslateAsync(profile,KeyVault.Unprotect(profile.ProtectedKey),regions,language,progress,token);
   if(closed||mine!=generation)return;translations=result;cache[fingerprint]=new(result);if(cache.Count>6)cache.Remove(cache.Keys.First());
   ApplyResult(true);status.Text=$"{(language=="zh"?"中文":"英文")} · {watch.Elapsed.TotalSeconds:F1} 秒 · 已展开完整译文 · 按住空格看原图";
   app.Settings.LastTarget=language;
  }
  catch(OperationCanceledException){}
  catch(Exception ex){if(!closed&&mine==generation)status.Text=ex.Message;}
  if(!closed)PlaceToolbar();
 }
 void ApplyResult(bool final)
 {
  canvas.Translations=translations;canvas.InvalidateVisual();reading=true;BuildReader();UpdateMode();
 }
 void UpdateMode()
 {
  canvas.Original=original||holding;canvas.InvalidateVisual();
  bool expanded=reading&&!original&&!holding;
  reader.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;view.Visibility=expanded?Visibility.Hidden:Visibility.Visible;
  var bounds=capture.Bounds;
  if(expanded)
  {
   var dpi=VisualTreeHelper.GetDpi(this);var work=Forms.Screen.FromRectangle(capture.Bounds).WorkingArea;
   int barHeight=(int)Math.Ceiling(toolbar.ActualHeight*VisualTreeHelper.GetDpi(toolbar).DpiScaleY);
   bounds=ReadingLayout.Fit(capture.Bounds,work,dpi.DpiScaleX,dpi.DpiScaleY,Math.Max(barHeight,80),width=>
   {paragraphs.Measure(new Size(width,double.PositiveInfinity));return paragraphs.DesiredSize.Height;});
  }
  if(bounds!=displayBounds){displayBounds=bounds;Native.Place(this,bounds.X,bounds.Y,bounds.Width,bounds.Height);}
  PlaceToolbar();if(IsActive)Keyboard.Focus(expanded?reader:canvas);
 }
 void BuildReader()
 {
  paragraphs.Children.Clear();
  foreach(var r in regions)
  {
   var text=translations.GetValueOrDefault(r.Id)??r.Text;var block=Ui.Text(text,16);block.LineHeight=25;
   var border=Ui.Card(block,new Thickness(12,9,12,9));border.Margin=new Thickness(0,0,0,8);border.Cursor=Cursors.Hand;border.ToolTip=new TextBlock{Text="原文："+r.Text+"\n\n译文："+(translations.GetValueOrDefault(r.Id)??"尚未翻译"),TextWrapping=TextWrapping.Wrap,MaxWidth=540};
   border.MouseLeftButtonUp+=(_,_)=>ShowPair(r);paragraphs.Children.Add(border);
  }
  canvas.MouseLeftButtonUp-=CanvasClick;canvas.MouseLeftButtonUp+=CanvasClick;
 }
 void CanvasClick(object sender,MouseButtonEventArgs e)
 {
  var p=e.GetPosition(canvas);var region=regions.FirstOrDefault(r=>p.X>=r.X&&p.X<=r.X+r.Width&&p.Y>=r.Y&&p.Y<=r.Y+r.Height);if(region is not null)ShowPair(region);
 }
 void ShowPair(TextRegion region)
 {
  var dialog=new Window{Title="原文与译文",Width=560,Height=380,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};var panel=new StackPanel{Margin=new Thickness(20)};
  Ui.Field(panel,"原文",new TextBox{Text=region.Text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  Ui.Field(panel,"译文",new TextBox{Text=translations.GetValueOrDefault(region.Id)??"尚未翻译",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};dialog.Show();
 }
 void ShowCopyMenu()
 {
  var menu=new ContextMenu();var originalItem=new MenuItem{Header="复制识别原文"};originalItem.Click+=(_,_)=>Ui.Copy(string.Join("\n\n",regions.Select(r=>r.Text)));menu.Items.Add(originalItem);
  var translatedItem=new MenuItem{Header="复制译文",IsEnabled=translations.Count>0};translatedItem.Click+=(_,_)=>Ui.Copy(string.Join("\n\n",regions.Where(r=>translations.ContainsKey(r.Id)).Select(r=>translations[r.Id])));menu.Items.Add(translatedItem);
  menu.IsOpen=true;
 }
 void ShowSaveMenu()
 {
  var menu=new ContextMenu();var originalItem=new MenuItem{Header="保存原图…"};originalItem.Click+=(_,_)=>SaveImage(false);menu.Items.Add(originalItem);
  var translatedItem=new MenuItem{Header="保存译图…",IsEnabled=translations.Count>0};translatedItem.Click+=(_,_)=>SaveImage(true);menu.Items.Add(translatedItem);menu.IsOpen=true;
 }
 void SaveImage(bool translated)
 {
  try
  {
   var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片|*.png",FileName=$"ScreenLingo-{DateTime.Now:yyyyMMdd-HHmmss}{(translated?"-"+target:"")}.png",AddExtension=true};
   if(Directory.Exists(app.Settings.ScreenshotDirectory))dialog.InitialDirectory=app.Settings.ScreenshotDirectory;
   if(dialog.ShowDialog(toolbar)!=true)return;BitmapSource image=capture.Image;
   if(translated)
   {
    // Export all translated text when reflow is needed; never export a clipped viewport.
    FrameworkElement render;
    if(reading)
    {
     var stack=new StackPanel{Width=Math.Clamp(capture.Image.PixelWidth,600,1400),Background=Brushes.White,Margin=new Thickness(0)};
     foreach(var r in regions){var t=Ui.Text(translations.GetValueOrDefault(r.Id)??r.Text,20);t.Margin=new Thickness(22,12,22,12);stack.Children.Add(t);}render=stack;
    }
    else{render=new TranslationCanvas(capture.Image){Regions=regions,Translations=translations};}
    render.Measure(new Size(reading?render.Width:capture.Image.PixelWidth,double.PositiveInfinity));render.Arrange(new Rect(new Point(),render.DesiredSize));render.UpdateLayout();
    if(render.ActualWidth*render.ActualHeight>40_000_000)throw new InvalidOperationException("译图过大，请使用复制译文。");
    var targetBitmap=new RenderTargetBitmap((int)Math.Ceiling(render.ActualWidth),(int)Math.Ceiling(render.ActualHeight),96,96,PixelFormats.Pbgra32);targetBitmap.Render(render);image=targetBitmap;
   }
   File.WriteAllBytes(dialog.FileName,Capture.Png(image));app.Settings.ScreenshotDirectory=Path.GetDirectoryName(dialog.FileName)!;app.SaveSettings();status.Text="已保存："+Path.GetFileName(dialog.FileName);
  }
  catch(Exception ex){status.Text=ex.Message;}
 }
}

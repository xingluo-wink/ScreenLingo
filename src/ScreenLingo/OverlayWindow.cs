using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;
using ScreenLingo.Core;
namespace ScreenLingo;

public sealed class OverlayWindow:Window
{
 enum Preview { Reading,Original,Layout }
 readonly IReaderHost host;readonly CaptureResult capture;readonly Grid stage=new();
 readonly ReadingContent reader=new();readonly TranslationCanvas canvas;readonly Viewbox imageView;
 readonly TextBlock status=Ui.Text("正在识别选区…",12,Ui.Muted);
 readonly Button font;ContextMenu? openMenu;
 readonly Dictionary<ReadingMode,Button> modeButtons=[];
 readonly Button originalButton,copyButton,cancelButton,retryButton;
 readonly CancellationTokenSource lifetime=new();CancellationTokenSource? translating;Task? recognition;
 readonly Dictionary<string,Dictionary<string,string>> cache=[];
 readonly Dictionary<string,BilingualAlignment> alignmentCache=[];
 BilingualAlignment? alignment;string lastStatus="正在识别选区…";
 List<TextRegion> regions=[];Dictionary<string,string> chinese=[],english=[];
 string profileKey="";int generation;bool closed,busy,requested,peek,manualBounds,autoFitted,renderQueued;
 Preview preview;HwndSource? source;
 public ReadingMode CurrentMode { get; private set; }
 public bool IsTranslating=>busy;
 public ReadingContent Reader=>reader;
 public bool HasManualBounds=>manualBounds;
 Dictionary<string,string> CurrentTranslations=>CurrentMode==ReadingMode.English?english:chinese;
 bool Bilingual=>CurrentMode==ReadingMode.Bilingual;
 bool HasTranslations=>Bilingual?chinese.Count>0||english.Count>0:CurrentTranslations.Count>0;
 bool EffectiveColumns=>Bilingual&&ReadingLayout.UseColumns(host.Settings.BilingualSideBySide,reader.ActualWidth,host.Settings.ReadingFontSize);

 public OverlayWindow(IReaderHost application,CaptureResult result)
 {
  host=application;capture=result;CurrentMode=host.Settings.BilingualDisplay?ReadingMode.Bilingual:host.Settings.LastTarget=="en"?ReadingMode.English:ReadingMode.Chinese;
  manualBounds=autoFitted=host.Settings.ReaderWidth>0&&host.Settings.ReaderHeight>0;
  Title="屏译 · 框选阅读";WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;
  ShowInTaskbar=true;Topmost=host.Settings.ReaderTopmost;Background=Brushes.White;Width=640;Height=440;MinWidth=540;MinHeight=230;
  UseLayoutRounding=true;SnapsToDevicePixels=true;
  canvas=new(result.Image);imageView=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,Visibility=Visibility.Hidden,Margin=new Thickness(12)};
  stage.Background=Ui.Background;stage.Children.Add(imageView);stage.Children.Add(reader);
  originalButton=CompactButton("原图",ToggleOriginal);copyButton=CompactButton("复制",CopyCurrent);copyButton.IsEnabled=false;
  font=CompactButton(host.Settings.ReadingFontSize+" ▾",ShowFontMenu);font.MinWidth=54;AutomationProperties.SetName(font,"阅读字号");
  cancelButton=CompactButton("停止",CancelTranslation);cancelButton.Visibility=Visibility.Collapsed;
  retryButton=CompactButton("重试",()=>_=TranslateAsync());retryButton.Visibility=Visibility.Collapsed;
  var root=new Grid{Background=Brushes.White};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
  var toolbar=new Border{Background=Brushes.White,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(12,9,12,7),Child=BuildToolbar()};root.Children.Add(toolbar);
  Grid.SetRow(stage,1);root.Children.Add(stage);
  var footer=new Grid{Margin=new Thickness(14,7,10,7)};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
  status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(status);
  var feedback=new StackPanel{Orientation=Orientation.Horizontal};feedback.Children.Add(cancelButton);feedback.Children.Add(retryButton);Grid.SetColumn(feedback,1);footer.Children.Add(feedback);Grid.SetRow(footer,2);root.Children.Add(footer);
  AutomationProperties.SetName(reader,"可选择复制的阅读正文");AutomationProperties.SetAutomationId(reader,"ReaderBody");
  reader.SizeChanged+=(_,_)=>QueueRenderIfLayoutChanged();
  reader.LinkedSelectionChanged+=(_,_)=>RefreshStatus();
  reader.PreviewMouseWheel+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control){ChangeFont(e.Delta>0?1:-1);e.Handled=true;}};
  SourceInitialized+=(_,_)=>
  {
   source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source?.AddHook(WindowMessages);
   var dpi=VisualTreeHelper.GetDpi(this);var work=Forms.Screen.FromRectangle(capture.Bounds).WorkingArea;
   MinWidth=Math.Min(540,(work.Width-24)/dpi.DpiScaleX);MinHeight=Math.Min(230,(work.Height-24)/dpi.DpiScaleY);
   Native.PlaceReader(this,ReadingLayout.Initial(capture.Bounds,work,dpi.DpiScaleX,dpi.DpiScaleY,host.Settings.ReaderWidth,host.Settings.ReaderHeight,host.Settings.BilingualSideBySide&&Bilingual));
  };
  Loaded+=async(_,_)=>{reader.Focus();recognition=RecognizeAsync();await recognition;};
  PreviewKeyDown+=OnKeyDown;PreviewKeyUp+=(_,e)=>{if(e.Key==Key.Space&&peek){peek=false;UpdatePreview();e.Handled=true;}};
  Deactivated+=(_,_)=>{if(peek){peek=false;UpdatePreview();}};
  StateChanged+=(_,_)=>{if(IsLoaded){manualBounds=true;autoFitted=true;}};
  Closed+=(_,_)=>
  {
   closed=true;generation++;lifetime.Cancel();translating?.Cancel();if(openMenu is not null)openMenu.IsOpen=false;source?.RemoveHook(WindowMessages);
   canvas.Clear();imageView.Child=null;reader.SetAlignment(null);reader.Document.Blocks.Clear();cache.Clear();alignmentCache.Clear();Content=null;
  };
 }
 static Button CompactButton(string text,Action action)
 {
  var button=Ui.Btn(text,action);button.Padding=new Thickness(9,5,9,5);button.MinHeight=30;button.Margin=new Thickness(0,0,4,2);return button;
 }
 UIElement BuildToolbar()
 {
  var row=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var modes=new StackPanel{Orientation=Orientation.Horizontal};
  foreach(var(mode,label)in new[]{(ReadingMode.Chinese,"中文"),(ReadingMode.English,"English"),(ReadingMode.Bilingual,"双语")})
  {
   var button=CompactButton(label,()=>_=SelectModeAsync(mode));button.Margin=new Thickness(1);button.Padding=new Thickness(12,5,12,5);button.BorderThickness=new Thickness(0);modeButtons[mode]=button;modes.Children.Add(button);
  }
  row.Children.Add(new Border{Background=Ui.Background,CornerRadius=new CornerRadius(9),Padding=new Thickness(2),Margin=new Thickness(0,0,10,2),Child=modes});
  var sizes=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,9,0),VerticalAlignment=VerticalAlignment.Center};
  var smaller=CompactButton("A−",()=>ChangeFont(-1));smaller.ToolTip="减小字号（Ctrl + 滚轮）";smaller.Padding=new Thickness(7,4,7,4);sizes.Children.Add(smaller);
  sizes.Children.Add(font);
  var larger=CompactButton("A＋",()=>ChangeFont(1));larger.ToolTip="增大字号（Ctrl + 滚轮）";larger.Padding=new Thickness(7,4,7,4);larger.Margin=new Thickness(4,0,0,2);sizes.Children.Add(larger);row.Children.Add(sizes);
  row.Children.Add(originalButton);row.Children.Add(copyButton);row.Children.Add(CompactButton("更多",ShowMoreMenu));UpdateModeButtons();return row;
 }
 void ShowFontMenu()
 {
  var menu=new ContextMenu();foreach(int size in ReadingContent.FontSizes)
  {
   var item=new MenuItem{Header=size.ToString(),IsCheckable=true,IsChecked=size==host.Settings.ReadingFontSize};item.Click+=(_,_)=>SetReadingFontSize(size);menu.Items.Add(item);
  }
  menu.PlacementTarget=font;menu.Placement=PlacementMode.Bottom;openMenu=menu;menu.IsOpen=true;
 }
 public void UseManualBounds()
 {
  manualBounds=true;autoFitted=true;
  if(WindowState==WindowState.Normal){host.Settings.ReaderWidth=ActualWidth;host.Settings.ReaderHeight=ActualHeight;SavePreferences();}
 }
 IntPtr WindowMessages(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
 {
  if(message==0x0231){manualBounds=true;autoFitted=true;}
  if(message==0x0232)UseManualBounds();return IntPtr.Zero;
 }
 void SetStatus(string text){lastStatus=text;RefreshStatus();}
 void RefreshStatus(){string text=Bilingual&&!busy&&reader.LinkedSelectionLabel.Length>0?reader.LinkedSelectionLabel:lastStatus;status.Text=text;status.ToolTip=text;}
 void SavePreferences(){try{host.SaveSettings();}catch(Exception ex){SetStatus("偏好未能保存："+ex.Message);}}
 void UpdateModeButtons()
 {
  foreach(var pair in modeButtons)
  {
   bool active=pair.Key==CurrentMode;pair.Value.Background=active?Ui.Accent:Brushes.Transparent;pair.Value.Foreground=active?Brushes.White:Ui.Ink;
   pair.Value.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;pair.Value.ToolTip=active?"当前模式；点击可继续未完成的翻译":"切换为"+(pair.Key==ReadingMode.Bilingual?"中英双语":pair.Key==ReadingMode.Chinese?"中文":"英文");
  }
 }
 public async Task SelectModeAsync(ReadingMode mode)
 {
  if(closed||(busy&&CurrentMode==mode))return;
  CurrentMode=mode;host.Settings.BilingualDisplay=Bilingual;if(!Bilingual)host.Settings.LastTarget=mode==ReadingMode.English?"en":"zh";
  preview=Preview.Reading;peek=false;UpdateModeButtons();SavePreferences();await TranslateAsync();
 }
 public void SetReadingFontSize(int size)
 {
  autoFitted=true;
  size=Math.Clamp(size,12,40);host.Settings.ReadingFontSize=size;font.Content=size+" ▾";
  preview=Preview.Reading;peek=false;Render();SavePreferences();
 }
 void ChangeFont(int direction)
 {
  int size=host.Settings.ReadingFontSize;SetReadingFontSize(direction>0?ReadingContent.FontSizes.FirstOrDefault(s=>s>size,40):ReadingContent.FontSizes.LastOrDefault(s=>s<size,12));
 }
 void QueueRenderIfLayoutChanged()
 {
  if(closed||renderQueued||reader.UsesColumns==EffectiveColumns)return;renderQueued=true;
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{renderQueued=false;if(!closed)Render();}));
 }
 void Render()
 {
  if(closed)return;reader.Present(regions,chinese,english,CurrentMode,EffectiveColumns,host.Settings.ReadingFontSize,!requested||!HasTranslations,busy);
  reader.SetAlignment(alignment);RefreshStatus();
  canvas.Regions=regions;canvas.Translations=CurrentTranslations;copyButton.IsEnabled=regions.Count>0;UpdatePreview();
 }
 void UpdatePreview()
 {
  bool showImage=peek||preview!=Preview.Reading;reader.Visibility=showImage?Visibility.Hidden:Visibility.Visible;imageView.Visibility=showImage?Visibility.Visible:Visibility.Hidden;
  canvas.Original=peek||preview!=Preview.Layout;canvas.InvalidateVisual();originalButton.Content=preview==Preview.Original?"正文":"原图";
 }
 public void ToggleOriginal(){autoFitted=true;preview=preview==Preview.Original?Preview.Reading:Preview.Original;peek=false;UpdatePreview();}
 public void ShowLayoutPreview()
 {
  if(!HasTranslations||Bilingual)return;
  canvas.Regions=regions;canvas.Translations=CurrentTranslations;
  // Check the full layout before displaying it; an overflowing block must never
  // be silently clipped into an apparently complete translation.
  if(CurrentTranslations.Count<regions.Count||!canvas.FitsAllText()){preview=Preview.Reading;SetStatus("原图位置放不下完整译文，已保留完整阅读。字号和窗口大小可自由调整。");}
  else{preview=Preview.Layout;SetStatus("原图布局预览 · 按 R 返回完整阅读");}peek=false;UpdatePreview();
 }
 async Task RecognizeAsync()
 {
  try
  {
   var response=await host.RecognizeAsync(Capture.Png(capture.Image),lifetime.Token);if(closed)return;
   if(response.Error is not null)throw new InvalidOperationException(response.Error);
   regions=LayoutGrouper.Group(response.Lines);Render();SetStatus(regions.Count==0?"未识别到文字，可保存原图或重新框选。":"已识别整个选区 · 选择中文、English 或双语开始翻译");
  }
  catch(OperationCanceledException){}
  catch(Exception ex){if(!closed)SetStatus(ex.Message);}
 }
 public async Task TranslateAsync()
 {
  if(closed)return;int mine=++generation;translating?.Cancel();var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);translating=request;
  var token=request.Token;busy=true;cancelButton.Visibility=Visibility.Visible;retryButton.Content="重试";retryButton.Visibility=Visibility.Collapsed;var selected=CurrentMode;
  try
  {
   SetStatus("正在准备翻译…");if(recognition is not null)await recognition;token.ThrowIfCancellationRequested();
   if(regions.Count==0){SetStatus("没有可翻译的文字，请重新框选。");return;}
   var profile=host.Settings.ActiveProfile?.Clone()??throw new ArgumentException("请先在设置中配置 API。");TranslationService.ValidateProfile(profile);
   string key=KeyVault.Unprotect(profile.ProtectedKey);var identity=profile.Clone();identity.ProtectedKey="";
   string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity)+"\n"+key)));
   if(profileKey!=fingerprint)
   {
    profileKey=fingerprint;chinese=cache.TryGetValue(fingerprint+"zh",out var zh)?new(zh):[];english=cache.TryGetValue(fingerprint+"en",out var en)?new(en):[];
    alignment=alignmentCache.GetValueOrDefault(fingerprint);
   }
   requested=true;Render();var watch=Stopwatch.StartNew();bool sent=false;string? alignmentProblem=null;using var api=host.CreateTranslationService();
   var selection=regions[0];
   if(selected==ReadingMode.Bilingual)
   {
    if(!chinese.ContainsKey(selection.Id)||!english.ContainsKey(selection.Id))
    {
     sent=true;SetStatus("正在整体翻译选区…");
     var result=await api.TranslateBilingualAsync(profile,key,selection,english.GetValueOrDefault(selection.Id),chinese.GetValueOrDefault(selection.Id),token);
     token.ThrowIfCancellationRequested();if(closed||mine!=generation)return;
     english[selection.Id]=result.English;chinese[selection.Id]=result.Chinese;
     cache[fingerprint+"en"]=new(english);cache[fingerprint+"zh"]=new(chinese);Render();
    }
    if(profile.Protocol!=ApiProtocol.QwenMt&&(alignment is null||alignment.Phrases.Count==0))
    {
     sent=true;SetStatus("译文已完成 · 正在补充词组高亮，可先阅读或复制");
     try
     {
      var result=await api.AlignBilingualAsync(profile,key,english[selection.Id],chinese[selection.Id],token);
      token.ThrowIfCancellationRequested();if(closed||mine!=generation)return;
      alignment=result;alignmentCache[fingerprint]=result;Render();
     }
     catch(OperationCanceledException){throw;}
     catch(Exception ex)
     {
      token.ThrowIfCancellationRequested();if(closed||mine!=generation)return;
      alignmentProblem=ex is OutputTruncatedException?"高亮结果仍超出输出上限，可重试高亮":"高亮暂未就绪："+ex.Message;
     }
    }
   }
   else
   {
    string language=selected==ReadingMode.English?"en":"zh";var current=language=="zh"?chinese:english;
    if(!current.ContainsKey(selection.Id))
    {
     sent=true;SetStatus("正在整体翻译选区…");var result=await api.TranslateAsync(profile,key,regions,language,null,token);
     token.ThrowIfCancellationRequested();if(closed||mine!=generation)return;
     foreach(var pair in result)current[pair.Key]=pair.Value;cache[fingerprint+language]=new(current);Render();
    }
   }
   while(cache.Count>6)cache.Remove(cache.Keys.First());while(alignmentCache.Count>3)alignmentCache.Remove(alignmentCache.Keys.First());
   string hint="可划选复制文字";
   if(selected==ReadingMode.Bilingual)
   {
    if(alignment?.Phrases.Count>0)hint="选中词句，另一语言同步高亮";
    else if(profile.Protocol==ApiProtocol.QwenMt)hint="Qwen-MT 接口仅提供翻译；通用模型接口支持联动高亮";
    else{hint="完整译文已保留 · "+(alignmentProblem??"模型未提供有效的词组对应关系，可重试高亮");retryButton.Content="重试高亮";retryButton.Visibility=Visibility.Visible;}
   }
   SetStatus((selected==ReadingMode.Bilingual?"中英对照":selected==ReadingMode.Chinese?"中文译文":"英文译文")+(sent?$" · {watch.Elapsed.TotalSeconds:F1} 秒":" · 已复用译文")+" · "+hint);
   if(!manualBounds&&!autoFitted&&reader.VerticalOffset<1&&reader.Selection.IsEmpty)FitToContent();
  }
  catch(OperationCanceledException){if(!closed&&mine==generation)SetStatus("已停止翻译，已完成内容已保留。");}
  catch(Exception ex){if(!closed&&mine==generation){SetStatus(ex.Message);retryButton.Visibility=Visibility.Visible;}}
  finally
  {
   if(ReferenceEquals(translating,request))translating=null;request.Dispose();
   if(!closed&&mine==generation){busy=false;cancelButton.Visibility=Visibility.Collapsed;Render();}
  }
 }
 public void CancelTranslation()
 {
  generation++;translating?.Cancel();busy=false;cancelButton.Visibility=Visibility.Collapsed;retryButton.Visibility=regions.Count>0?Visibility.Visible:Visibility.Collapsed;
  retryButton.Content=Bilingual&&chinese.Count>0&&english.Count>0?"重试高亮":"重试";
  Render();SetStatus("已停止翻译，已完成内容已保留。重试只补充缺失内容。");
 }
 public ReadingContent CreateExportContent(double width)
 {
  var result=new ReadingContent{Width=Math.Max(160,width),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
  result.Present(regions,chinese,english,CurrentMode,ReadingLayout.UseColumns(Bilingual&&host.Settings.BilingualSideBySide,width,host.Settings.ReadingFontSize),host.Settings.ReadingFontSize,!requested||!HasTranslations);
  result.Measure(new Size(result.Width,double.PositiveInfinity));result.Arrange(new Rect(new Point(),result.DesiredSize));result.UpdateLayout();return result;
 }
 public void FitToContent()
 {
  if(closed||WindowState!=WindowState.Normal)return;autoFitted=true;
  var content=CreateExportContent(reader.ActualWidth>0?reader.ActualWidth:Width-16);var dpi=VisualTreeHelper.GetDpi(this);
  var work=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
  double chrome=Math.Max(90,ActualHeight-reader.ActualHeight);double height=Math.Clamp(content.DesiredSize.Height+chrome+6,MinHeight,Math.Max(MinHeight,(work.Height-24)/dpi.DpiScaleY));
  var bounds=Native.ReaderBounds(this);bounds.Height=(int)Math.Ceiling(height*dpi.DpiScaleY);bounds=ReadingLayout.Constrain(bounds,work);
  Native.PlaceReader(this,bounds);
 }
 void CopyCurrent()
 {
  if(!reader.Selection.IsEmpty&&reader.IsVisible){Ui.Copy(reader.Selection.Text);SetStatus("已复制选中文字。");return;}
  bool copySource=!requested||!HasTranslations||preview==Preview.Original||peek;
  string text=copySource?string.Join("\n\n",regions.Select(r=>r.Text)):Bilingual?ReadingContent.BilingualText(regions,chinese,english):string.Join("\n\n",regions.Select(r=>CurrentTranslations.GetValueOrDefault(r.Id)??"尚无译文"));
  if(text.Length>0){Ui.Copy(text);SetStatus(copySource?"已复制识别原文。":Bilingual?"已复制中英对照。":"已复制正文。");}
 }
 void ShowMoreMenu()
 {
  var menu=new ContextMenu();
  void Add(string label,Action action,bool enabled=true){var item=new MenuItem{Header=label,IsEnabled=enabled};item.Click+=(_,_)=>action();menu.Items.Add(item);}
  var pin=new MenuItem{Header="窗口置顶",IsCheckable=true,IsChecked=Topmost};pin.Click+=(_,_)=>{Topmost=pin.IsChecked;host.Settings.ReaderTopmost=Topmost;SavePreferences();};menu.Items.Add(pin);
  var columns=new MenuItem{Header="双语左右对照（拉宽窗口时）",IsCheckable=true,IsChecked=host.Settings.BilingualSideBySide};columns.Click+=(_,_)=>{host.Settings.BilingualSideBySide=columns.IsChecked;Render();SavePreferences();if(Bilingual&&!EffectiveColumns&&columns.IsChecked)SetStatus("拉宽窗口后显示左右对照；当前宽度使用上下对照。");};menu.Items.Add(columns);
  Add("窗口适应内容",()=>{manualBounds=false;host.Settings.ReaderWidth=0;host.Settings.ReaderHeight=0;FitToContent();SavePreferences();});
  Add("原图布局预览",ShowLayoutPreview,HasTranslations&&!Bilingual);menu.Items.Add(new Separator());
  Add("复制识别原文",()=>Ui.Copy(string.Join("\n\n",regions.Select(r=>r.Text))),regions.Count>0);
  Add("复制中英对照",()=>Ui.Copy(ReadingContent.BilingualText(regions,chinese,english)),chinese.Count>0&&english.Count>0);
  Add("保存原图…    Ctrl+S",()=>SaveImage(false));Add("保存完整译图…    Ctrl+Shift+S",()=>SaveImage(true),HasTranslations);menu.Items.Add(new Separator());
  Add("API 设置",()=>{host.ShowSettings();});Add("关闭窗口    Esc",Close);menu.PlacementTarget=this;menu.Placement=PlacementMode.MousePoint;openMenu=menu;menu.IsOpen=true;
 }
 void OnKeyDown(object sender,KeyEventArgs e)
 {
  var key=e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;var modifiers=Keyboard.Modifiers;
  if(openMenu?.IsOpen==true||IsMenu(e.OriginalSource as DependencyObject))return;
  if(key==Key.Escape){Close();e.Handled=true;return;}
  if(IsMenuOrCombo(e.OriginalSource as DependencyObject))return;
  if(key==Key.S&&modifiers==ModifierKeys.Control){SaveImage(false);e.Handled=true;}
  else if(key==Key.S&&modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){if(HasTranslations)SaveImage(true);e.Handled=true;}
  else if(key==Key.C&&modifiers==ModifierKeys.Control&&reader.Selection.IsEmpty){CopyCurrent();e.Handled=true;}
  else if(modifiers==ModifierKeys.None)
  {
   if(e.IsRepeat&&key is Key.C or Key.E or Key.B or Key.O or Key.R){e.Handled=true;return;}
   if(key==Key.Space){peek=true;UpdatePreview();e.Handled=true;}
   else if(key==Key.C){_=SelectModeAsync(ReadingMode.Chinese);e.Handled=true;}
   else if(key==Key.E){_=SelectModeAsync(ReadingMode.English);e.Handled=true;}
   else if(key==Key.B){_=SelectModeAsync(ReadingMode.Bilingual);e.Handled=true;}
   else if(key==Key.O){ToggleOriginal();e.Handled=true;}
   else if(key==Key.R){preview=Preview.Reading;peek=false;UpdatePreview();e.Handled=true;}
  }
 }
 static bool IsMenuOrCombo(DependencyObject? item)
 {
  while(item is not null){if(item is ComboBox or ComboBoxItem or MenuItem)return true;item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);}return false;
 }
 static bool IsMenu(DependencyObject? item)
 {
  while(item is not null){if(item is MenuItem or System.Windows.Controls.ContextMenu)return true;item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);}return false;
 }
 void SaveImage(bool translated)
 {
  try
  {
   var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片|*.png",FileName=$"ScreenLingo-{DateTime.Now:yyyyMMdd-HHmmss}{(translated?Bilingual?"-zh-en":CurrentMode==ReadingMode.English?"-en":"-zh":"")}.png",AddExtension=true};
   if(Directory.Exists(host.Settings.ScreenshotDirectory))dialog.InitialDirectory=host.Settings.ScreenshotDirectory;
   if(dialog.ShowDialog(this)!=true)return;BitmapSource image=capture.Image;
   if(translated)
   {
    var render=CreateExportContent(reader.ActualWidth>0?reader.ActualWidth:640);
    if(render.ActualWidth*render.ActualHeight>40_000_000)throw new InvalidOperationException("译图过大，请使用复制正文。");
    var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(render.ActualWidth)),Math.Max(1,(int)Math.Ceiling(render.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(render);image=bitmap;
   }
   File.WriteAllBytes(dialog.FileName,Capture.Png(image));host.Settings.ScreenshotDirectory=Path.GetDirectoryName(dialog.FileName)!;SavePreferences();SetStatus("已保存："+Path.GetFileName(dialog.FileName));
  }
  catch(Exception ex){SetStatus(ex.Message);}
 }
}

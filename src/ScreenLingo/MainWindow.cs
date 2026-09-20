using System.Diagnostics;
using System.Windows.Input;
using ScreenLingo.Core;
namespace ScreenLingo;

public sealed class MainWindow:Window
{
 readonly ScreenApp app;
 readonly ListBox profiles=new(){MinWidth=160,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
 readonly TextBox name=new(),url=new(),model=new(),timeout=new(),tokens=new(),extra=new(){AcceptsReturn=true,Height=88,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas")},style=new(){AcceptsReturn=true,Height=60,TextWrapping=TextWrapping.Wrap};
 readonly PasswordBox key=new();
 readonly CheckBox streaming=new(){Content="边生成边显示译文与高亮",Margin=new Thickness(0,10,0,6)};
 readonly ComboBox protocol=new(),idle=new();
 readonly TextBox hotkey=new(){IsReadOnly=true};
 readonly TextBlock status=Ui.Text("填写 API 后点击「测试翻译」，或直接框选体验本地文字识别。",12,Ui.Muted);
 readonly TextBlock resources=Ui.Text("",12,Ui.Muted);
 ApiProfile? current;bool loading;CancellationTokenSource? test;
 public MainWindow(ScreenApp application)
 {
  app=application;Title="屏译 ScreenLingo";Width=920;Height=Math.Min(850,SystemParameters.WorkArea.Height-50);MinWidth=760;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  var root=new Grid{Margin=new Thickness(28,22,28,18)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
  var heading=new DockPanel{Margin=new Thickness(0,0,0,20)};var version=Ui.Text("v0.3.2  /  LOCAL OCR",11,Ui.Muted);version.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(version,Dock.Right);heading.Children.Add(version);
  var brand=new StackPanel();brand.Children.Add(Ui.Text("屏译  ScreenLingo",27));brand.Children.Add(Ui.Text("框选眼前的内容，用熟悉的语言阅读。",12,Ui.Muted));heading.Children.Add(brand);root.Children.Add(heading);
  var hero=new DockPanel();var capture=Ui.Btn("＋  框选屏幕",()=>app.BeginCapture(),true);capture.MinWidth=155;capture.FontSize=15;DockPanel.SetDock(capture,Dock.Right);hero.Children.Add(capture);
  var introduction=new StackPanel();introduction.Children.Add(Ui.Text("随时框选，即刻开始",16));introduction.Children.Add(Ui.Text("中文 / English · 中英双语 · 字号可调 · 截图独立保存",12,Ui.Muted));hero.Children.Add(introduction);
  var card=Ui.Card(hero);card.Margin=new Thickness(0,0,0,18);Grid.SetRow(card,1);root.Children.Add(card);
  var tabs=new TabControl{Background=Brushes.Transparent,BorderBrush=Ui.Line};Grid.SetRow(tabs,2);root.Children.Add(tabs);
  tabs.Items.Add(new TabItem{Header="翻译 API",Content=ApiPanel()});tabs.Items.Add(new TabItem{Header="快捷键与性能",Content=PerformancePanel()});tabs.Items.Add(new TabItem{Header="使用说明",Content=HelpPanel()});
  var footer=new DockPanel{Margin=new Thickness(0,15,0,0)};var hide=Ui.Btn("收起到托盘",Close);DockPanel.SetDock(hide,Dock.Right);footer.Children.Add(hide);footer.Children.Add(resources);Grid.SetRow(footer,3);root.Children.Add(footer);
  profiles.SelectionChanged+=(_,_)=>SelectProfile();
  protocol.SelectionChanged+=(_,_)=>UpdateProtocolHint();
  RefreshProfiles(app.Settings.ActiveProfile);
  var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
  timer.Tick+=(_,_)=>RefreshResources();IsVisibleChanged+=(_,_)=>{if(IsVisible){RefreshResources();timer.Start();}else timer.Stop();};
  Closing+=(_,e)=>{try{StoreDraft();app.SaveSettings();}catch(Exception ex){MessageBox.Show(ex.Message,"配置保存失败");}test?.Cancel();timer.Stop();};
  if(app.Store.LoadWarning is not null)status.Text=app.Store.LoadWarning;
 }
 UIElement ApiPanel()
 {
  var layout=new Grid{Margin=new Thickness(18)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(180)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(22)});layout.ColumnDefinitions.Add(new());
  var sidebar=new DockPanel();var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};
  buttons.Children.Add(Ui.Btn("新增",()=>{StoreDraft();var p=new ApiProfile{Name="新配置 "+(app.Settings.Profiles.Count+1)};app.Settings.Profiles.Add(p);RefreshProfiles(p);}));
  buttons.Children.Add(Ui.Btn("移除",()=>{if(current is null||app.Settings.Profiles.Count<=1){status.Text="至少保留一套配置。";return;}var p=current;current=null;app.Settings.Profiles.Remove(p);RefreshProfiles(app.Settings.Profiles[0]);}));
  DockPanel.SetDock(buttons,Dock.Bottom);sidebar.Children.Add(buttons);sidebar.Children.Add(profiles);layout.Children.Add(sidebar);
  var right=new DockPanel();Grid.SetColumn(right,2);layout.Children.Add(right);
  var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};bottom.Children.Add(status);status.Margin=new Thickness(0,0,0,10);
  var actions=new WrapPanel();actions.Children.Add(Ui.Btn("保存并使用",Save,true));actions.Children.Add(Ui.Btn("测试翻译",async()=>await TestAsync()));actions.Children.Add(Ui.Btn("取消测试",()=>test?.Cancel()));bottom.Children.Add(actions);DockPanel.SetDock(bottom,Dock.Bottom);right.Children.Add(bottom);
  var form=new StackPanel();var scroll=new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(0,0,12,0)};right.Children.Add(scroll);
  protocol.ItemsSource=new[]{"Chat Completions（常见兼容接口）","Responses","Anthropic Messages","Gemini GenerateContent","Qwen-MT（专用翻译）"};
  Ui.Field(form,"配置名称",name);Ui.Field(form,"接口类型",protocol);
  Ui.Field(form,"API 地址",url,"填写服务商提供的基础地址（通常含 /v1）；也支持完整接口地址。");
  Ui.Field(form,"API Key",key,"密钥在本机加密保存；本机免密接口可以留空。");Ui.Field(form,"模型名称",model,"按服务商提供的模型 ID 原样填写。");
  var advanced=new StackPanel();Ui.Field(advanced,"请求超时（秒）",timeout);Ui.Field(advanced,"最大输出 Token",tokens);
  advanced.Children.Add(streaming);advanced.Children.Add(Ui.Text("默认开启。接口不支持流式响应时可关闭；Qwen-MT 使用完整返回。",11,Ui.Muted));
  Ui.Field(advanced,"翻译风格",style);Ui.Field(advanced,"额外请求参数（JSON）",extra,"例如 {\"temperature\":0.2}。模型、输入和目标语言由软件管理。");
  form.Children.Add(new Expander{Header="高级设置",Content=advanced,Margin=new Thickness(0,16,0,0)});return layout;
 }
 UIElement PerformancePanel()
 {
  var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(Ui.Text("轻量待机，按需识别",19));
  Ui.Field(panel,"全局框选快捷键",hotkey,"点击输入框后按下新的组合，例如 Ctrl + Alt + Q。");hotkey.Text=app.Settings.Hotkey;
  hotkey.PreviewKeyDown+=(_,e)=>
  {
   e.Handled=true;var k=e.Key==Key.System?e.SystemKey:e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;var mod=Keyboard.Modifiers;
   if(k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;
   string value=(mod.HasFlag(ModifierKeys.Control)?"Ctrl+":"")+(mod.HasFlag(ModifierKeys.Alt)?"Alt+":"")+(mod.HasFlag(ModifierKeys.Shift)?"Shift+":"")+k;
   try{Native.ParseHotkey(value);hotkey.Text=value;}catch(Exception ex){status.Text=ex.Message;}
  };
  idle.ItemsSource=new[]{"30 秒","1 分钟","2 分钟（推荐）","5 分钟"};idle.SelectedIndex=app.Settings.IdleSeconds switch{30=>0,60=>1,300=>3,_=>2};
  Ui.Field(panel,"闲置多久释放 OCR",idle,"连续框选时复用模型；闲置后退出识别进程，下次使用时重新加载。");
  var row=new WrapPanel{Margin=new Thickness(0,22,0,16)};row.Children.Add(Ui.Btn("保存设置",Save,true));row.Children.Add(Ui.Btn("立即释放 OCR",()=>{app.Ocr.ReleaseNow();RefreshResources();}));panel.Children.Add(row);
  panel.Children.Add(Ui.Text("识别只使用本机 CPU。截图默认留在内存中；点击语言按钮时，仅识别出的选区文字发送到当前 API。",13,Ui.Muted));
  var location=Ui.Text("程序与配置位置：\n"+AppContext.BaseDirectory,11,Ui.Muted);location.Margin=new Thickness(0,18,0,0);panel.Children.Add(location);
  return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 UIElement HelpPanel()
 {
  var panel=new StackPanel{Margin=new Thickness(26)};
  foreach(var(title,body)in new[]{
   ("01  框选屏幕","按快捷键或点击「框选屏幕」，拖动选择范围，松开完成。Esc 随时退出。"),
   ("02  选择阅读模式","点击「中文」「English」或「双语」，当前模式会高亮。翻译等待期间仍可阅读识别原文；已完成译文会复用，重试只补充缺失内容。"),
   ("03  自由调整窗口","拖动系统标题栏移动窗口，拖动边缘调整大小，尺寸会记住。工具条与正文一起移动；更新译文和调整字号不会重置手动位置。更多菜单提供置顶开关和「窗口适应内容」。"),
   ("字号与中英双语","使用 A− / A＋ 或字号菜单选择 12–40；正文上 Ctrl+滚轮也可调整。整个选区一起翻译，英文在上、中文在下；更多菜单可选宽窗口左右对照，窄窗口或大字时自动改为上下对照。词组对应逐步就绪后，选中一侧文字，另一侧同步高亮。"),
   ("原图和原图布局","点击「原图」或按住空格查看截图，阅读窗口的位置与大小保持不变。「更多 → 原图布局预览」按截图排字；放不下完整译文时会保留完整阅读。"),
   ("键盘操作","C 中文、E 英文、B 双语、O 切换原图、R 返回正文；Ctrl+C 复制选中文字或正文；Ctrl+S 保存原图，Ctrl+Shift+S 保存完整译图；Esc 关闭窗口。"),
   ("04  保存是独立操作","「更多 → 保存原图」保存所选截图；「保存完整译图」保存全部文字，含滚动区域外的内容，并保留字号及双语排版。"),
   ("05  固定的是这一刻的画面","译文层不会跟随底层网页滚动。退出后重新框选即可。受保护的视频、系统安全桌面和独占全屏程序可能无法截取。"),
   ("API 地址提示","Chat / Responses / Anthropic 通常填含 /v1 的地址；Gemini 通常含 /v1beta。HTTP 仅限 localhost。本工具不附带任何 API 额度。")})
  {var h=Ui.Text(title,15);h.Margin=new Thickness(0,0,0,6);panel.Children.Add(h);var b=Ui.Text(body,13,Ui.Muted);b.Margin=new Thickness(0,0,0,20);panel.Children.Add(b);}
  return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 void RefreshProfiles(ApiProfile? selected){loading=true;profiles.ItemsSource=null;profiles.ItemsSource=app.Settings.Profiles;profiles.SelectedItem=selected;loading=false;SelectProfile();}
 void SelectProfile()
 {
  if(loading)return;StoreDraft();current=profiles.SelectedItem as ApiProfile;if(current is null)return;
  name.Text=current.Name;url.Text=current.BaseUrl;model.Text=current.Model;protocol.SelectedIndex=(int)current.Protocol;timeout.Text=current.TimeoutSeconds.ToString();tokens.Text=current.MaxOutputTokens.ToString();extra.Text=current.ExtraBody;style.Text=current.Style;
  streaming.IsChecked=current.StreamResponses;
  try{key.Password=KeyVault.Unprotect(current.ProtectedKey);}catch(Exception ex){key.Password="";status.Text=ex.Message;}
 }
 void StoreDraft()
 {
  if(current is null)return;
  current.Name=string.IsNullOrWhiteSpace(name.Text)?"我的 API":name.Text.Trim();current.BaseUrl=url.Text.Trim();current.Model=model.Text.Trim();current.Protocol=(ApiProtocol)Math.Max(0,protocol.SelectedIndex);current.ProtectedKey=KeyVault.Protect(key.Password);
  current.TimeoutSeconds=int.TryParse(timeout.Text,out var seconds)?seconds:60;current.MaxOutputTokens=int.TryParse(tokens.Text,out var count)?count:4096;current.ExtraBody=extra.Text;current.Style=style.Text;
  current.StreamResponses=streaming.IsChecked==true;
 }
 void Save()
 {
  try
  {
   StoreDraft();if(current is not null)app.Settings.ActiveProfileId=current.Id;
   if(hotkey.Text!=app.Settings.Hotkey){app.ChangeHotkey(hotkey.Text);app.Settings.Hotkey=hotkey.Text;}
   app.Settings.IdleSeconds=idle.SelectedIndex switch{0=>30,1=>60,3=>300,_=>120};app.Ocr.IdleSeconds=app.Settings.IdleSeconds;
   app.SaveSettings();profiles.Items.Refresh();status.Text="已保存。当前配置："+app.Settings.ActiveProfile?.Name;RefreshResources();
  }
  catch(Exception ex){MessageBox.Show(this,ex.Message,"设置未保存",MessageBoxButton.OK,MessageBoxImage.Information);}
 }
 async Task TestAsync()
 {
  test?.Cancel();test=new();var mine=test;StoreDraft();if(current is null)return;var p=current.Clone();
  status.Text="正在翻译测试句：Learning a little every day makes a difference.";var watch=Stopwatch.StartNew();
  try
  {
   using var api=new TranslationService();var result=await api.TranslateAsync(p,key.Password,[new("b1","Learning a little every day makes a difference.",0,0,500,30,24,1)],"zh",null,mine.Token);
   if(test==mine)status.Text=$"连接成功 · {watch.Elapsed.TotalSeconds:F1} 秒\n{result["b1"]}";
  }
  catch(OperationCanceledException){if(test==mine)status.Text="测试已取消。";}
  catch(Exception ex){if(test==mine)status.Text=ex.Message;}
 }
 void UpdateProtocolHint(){url.ToolTip=protocol.SelectedIndex==3?"例如 https://generativelanguage.googleapis.com/v1beta":"例如 https://服务商域名/v1";}
 void RefreshResources()
 {
  try{long bytes=Process.GetCurrentProcess().WorkingSet64;if(app.Ocr.WorkerPid is int pid){using var worker=Process.GetProcessById(pid);bytes+=worker.WorkingSet64;}
   resources.Text=$"{app.Settings.Hotkey}  ·  内存约 {bytes/1048576d:F0} MB  ·  OCR {(app.Ocr.IsLoaded?"已加载":"休眠中")}";
  }catch{resources.Text=app.Settings.Hotkey;}
 }
}

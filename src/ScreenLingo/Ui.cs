using System.Windows.Automation;
using System.Windows.Markup;
namespace ScreenLingo;
public static class Ui
{
 public static readonly SolidColorBrush Accent=new(Color.FromRgb(15,118,110));
 public static readonly SolidColorBrush Ink=new(Color.FromRgb(24,45,48));
 public static readonly SolidColorBrush Muted=new(Color.FromRgb(98,114,118));
 public static readonly SolidColorBrush Background=new(Color.FromRgb(245,248,247));
 public static readonly SolidColorBrush Line=new(Color.FromRgb(219,229,226));
 public static void Install(Application app)
 {
  var resources=(ResourceDictionary)XamlReader.Parse("""
  <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
   <Style TargetType="Window"><Setter Property="FontFamily" Value="Microsoft YaHei UI"/><Setter Property="FontSize" Value="13"/><Setter Property="Foreground" Value="#182D30"/><Setter Property="Background" Value="#F5F8F7"/></Style>
   <Style TargetType="Button"><Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#182D30"/><Setter Property="BorderBrush" Value="#DBE5E2"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="15,9"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Cursor" Value="Hand"/><Setter Property="MinHeight" Value="36"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bd" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="bd" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
   </Style>
   <Style TargetType="TextBox"><Setter Property="Padding" Value="10,7"/><Setter Property="BorderBrush" Value="#CCDCD7"/><Setter Property="BorderThickness" Value="1"/><Setter Property="MinHeight" Value="35"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="Background" Value="White"/></Style>
   <Style TargetType="PasswordBox"><Setter Property="Padding" Value="10,7"/><Setter Property="BorderBrush" Value="#CCDCD7"/><Setter Property="MinHeight" Value="35"/></Style>
   <Style TargetType="ComboBox"><Setter Property="Padding" Value="8,6"/><Setter Property="MinHeight" Value="35"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
   <Style TargetType="TabItem"><Setter Property="Padding" Value="20,10"/></Style>
   <Style TargetType="ToolTip"><Setter Property="MaxWidth" Value="600"/></Style>
  </ResourceDictionary>
  """);
  app.Resources.MergedDictionaries.Add(resources);
 }
 public static TextBlock Text(string text,double size=13,Brush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap};
 public static Button Btn(string text,Action click,bool primary=false){var b=new Button{Content=text};AutomationProperties.SetName(b,text);if(primary){b.Background=Accent;b.Foreground=Brushes.White;b.BorderBrush=Accent;}b.Click+=(_,_)=>click();return b;}
 public static Border Card(UIElement child,Thickness? padding=null)=>new(){Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=padding??new Thickness(20),Child=child};
 public static void Field(Panel parent,string label,FrameworkElement field,string? hint=null)
 {
  var title=Text(label,12,Muted);title.Margin=new Thickness(0,12,0,6);parent.Children.Add(title);AutomationProperties.SetName(field,label);parent.Children.Add(field);
  if(hint is not null){var h=Text(hint,11,Muted);h.Margin=new Thickness(0,5,0,0);parent.Children.Add(h);}
 }
 public static void Copy(string text){try{Clipboard.SetText(text);}catch(System.Runtime.InteropServices.ExternalException){MessageBox.Show("剪贴板暂时被其他程序占用，请重试。","屏译");}}
}

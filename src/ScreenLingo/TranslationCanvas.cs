using System.Globalization;
using System.Windows.Media.Imaging;
using ScreenLingo.Core;
namespace ScreenLingo;
public sealed class TranslationCanvas:FrameworkElement
{
 BitmapSource? image;public IReadOnlyList<TextRegion> Regions{get;set;}=[];public IReadOnlyDictionary<string,string> Translations{get;set;}=new Dictionary<string,string>();
 public bool Original{get;set;}public bool HasOverflow{get;private set;}
 readonly Dictionary<string,(SolidColorBrush Background,Brush Foreground)> colors=[];
 public TranslationCanvas(BitmapSource image){this.image=image;Width=image.PixelWidth;Height=image.PixelHeight;}
 public void Clear(){image=null;Regions=[];Translations=new Dictionary<string,string>();colors.Clear();InvalidateVisual();}
 protected override void OnRender(DrawingContext dc)
 {
  if(image is null)return;HasOverflow=false;dc.DrawImage(image,new Rect(0,0,Width,Height));if(Original)return;
  foreach(var region in Regions)
  {
   if(!Translations.TryGetValue(region.Id,out var translated))continue;
   var rect=new Rect(Math.Max(0,region.X-2),Math.Max(0,region.Y-1),Math.Min(Width-Math.Max(0,region.X-2),region.Width+4),Math.Min(Height-Math.Max(0,region.Y-1),region.Height+2));
   if(rect.Width<4||rect.Height<4)continue;
   var(background,foreground)=SampleColors(region);dc.DrawRectangle(background,null,rect);
   double font=Math.Clamp(region.FontHeight*.72,12,42);FormattedText text=Format(translated,font,foreground,Math.Max(2,rect.Width-4));
   while(text.Height>rect.Height-2 && font>12){font-=.5;text=Format(translated,font,foreground,Math.Max(2,rect.Width-4));}
   bool overflow=text.Height>rect.Height-1||text.MinWidth>rect.Width;
   HasOverflow|=overflow;
   dc.PushClip(new RectangleGeometry(rect));dc.DrawText(text,new Point(rect.X+2,rect.Y));dc.Pop();
   if(overflow)dc.DrawRectangle(Ui.Accent,null,new Rect(Math.Max(rect.Left,rect.Right-5),rect.Top,4,rect.Height));
  }
 }
 FormattedText Format(string text,double size,Brush color,double width)=>new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,color,1){MaxTextWidth=width};
 (SolidColorBrush,Brush) SampleColors(TextRegion region)
 {
  if(colors.TryGetValue(region.Id,out var color))return color;
  var bgra=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var samples=new List<(byte B,byte G,byte R)>();
  // Sample above/below the glyph rectangle, avoiding the text itself.
  for(int i=0;i<8;i++)foreach(int y in new[]{(int)region.Y-3,(int)(region.Y+region.Height)+2})
  {
   int x=Math.Clamp((int)(region.X+region.Width*i/8),0,image!.PixelWidth-1);int yy=Math.Clamp(y,0,image!.PixelHeight-1);var pixel=new byte[4];bgra.CopyPixels(new Int32Rect(x,yy,1,1),pixel,4,0);samples.Add((pixel[0],pixel[1],pixel[2]));
  }
  byte r=samples.Select(c=>c.R).Order().ElementAt(samples.Count/2),g=samples.Select(c=>c.G).Order().ElementAt(samples.Count/2),b=samples.Select(c=>c.B).Order().ElementAt(samples.Count/2);
  var background=new SolidColorBrush(Color.FromRgb(r,g,b));background.Freeze();Brush fg=(r*.299+g*.587+b*.114)>140?Brushes.Black:Brushes.White;colors[region.Id]=(background,fg);return(background,fg);
 }
}

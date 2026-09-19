namespace ScreenLingo;

public static class ReadingLayout
{
 public static System.Drawing.Rectangle Initial(System.Drawing.Rectangle selection,System.Drawing.Rectangle screen,
  double scaleX,double scaleY,double savedWidth,double savedHeight,bool columns)
 {
  double width=savedWidth>0?savedWidth:Math.Clamp(selection.Width/scaleX+48,columns?780:600,columns?980:840);
  double height=savedHeight>0?savedHeight:Math.Clamp(selection.Height/scaleY+160,340,640);
  double maxWidth=Math.Max(1,(screen.Width-24)/scaleX),maxHeight=Math.Max(1,(screen.Height-24)/scaleY);
  width=Math.Clamp(width,Math.Min(540,maxWidth),maxWidth);height=Math.Clamp(height,Math.Min(230,maxHeight),maxHeight);
  return Constrain(new(selection.Left,selection.Top,(int)Math.Ceiling(width*scaleX),(int)Math.Ceiling(height*scaleY)),screen);
 }
 public static System.Drawing.Rectangle Constrain(System.Drawing.Rectangle bounds,System.Drawing.Rectangle screen)
 {
  int margin=Math.Min(12,Math.Max(0,Math.Min(screen.Width,screen.Height)/8));
  int width=Math.Clamp(bounds.Width,1,Math.Max(1,screen.Width-2*margin)),height=Math.Clamp(bounds.Height,1,Math.Max(1,screen.Height-2*margin));
  int x=Math.Clamp(bounds.Left,screen.Left+margin,Math.Max(screen.Left+margin,screen.Right-margin-width));
  int y=Math.Clamp(bounds.Top,screen.Top+margin,Math.Max(screen.Top+margin,screen.Bottom-margin-height));return new(x,y,width,height);
 }
 public static bool UseColumns(bool requested,double contentWidth,int fontSize)=>requested&&contentWidth>=Math.Max(660,fontSize*25);
}

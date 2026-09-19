namespace ScreenLingo;

public static class ReadingLayout
{
 // Width and text measurement are in WPF units; the returned placement uses
 // physical screen pixels. The screenshot rectangle is only an anchor.
 public static System.Drawing.Rectangle Fit(System.Drawing.Rectangle selection,System.Drawing.Rectangle screen,
  double scaleX,double scaleY,int toolbarHeight,Func<double,double> measureContent)
 {
  int margin=12,gap=8;
  double maxWidth=Math.Max(1,(screen.Width-margin*2)/scaleX);
  double maxHeight=Math.Max(1,(screen.Height-toolbarHeight-gap-margin*2)/scaleY);
  double width=Math.Min(maxWidth,Math.Clamp(selection.Width/scaleX,420,760));
  double height=measureContent(Math.Max(1,width-28))+28;
  while(height>maxHeight && width<Math.Min(maxWidth,960))
  {
   width=Math.Min(Math.Min(maxWidth,960),width+120);
   height=measureContent(Math.Max(1,width-28))+28;
  }
  int w=Math.Min(screen.Width-margin*2,(int)Math.Ceiling(width*scaleX));
  int h=(int)Math.Ceiling(Math.Min(maxHeight,Math.Max(92,height))*scaleY);
  int x=Math.Clamp(selection.Left,screen.Left+margin,Math.Max(screen.Left+margin,screen.Right-margin-w));
  int top=screen.Top+margin+toolbarHeight+gap;
  int y=Math.Clamp(selection.Top,top,Math.Max(top,screen.Bottom-margin-h));
  return new(x,y,w,h);
 }
}

using MemoApp.Core.Storage;
namespace MemoApp.Core.Lifecycle;
// Native pixels only. Font/view zoom never enters these calculations.
public readonly record struct PixelWorkArea(int Left,int Top,int Width,int Height);
public readonly record struct PixelWindowRect(int X,int Y,int Width,int Height);
public static class DesktopLayoutGeometry
{
    public static PixelWindowRect Provisional(PixelWorkArea work,int oldWidth,int oldHeight)
    {
        Check(work);int width=Math.Clamp(oldWidth,1,work.Width),height=Math.Clamp(oldHeight,1,work.Height);
        return new(work.Left+(work.Width-width)/2,work.Top+(work.Height-height)/2,width,height);
    }
    public static PixelWindowRect Restore(PixelWorkArea work,StoredWindowLayout layout,double dpi)
    {
        Check(work);
        if(!double.IsFinite(dpi)||dpi is <48 or >768||!double.IsFinite(layout.X)||layout.X is <0 or >1||!double.IsFinite(layout.Y)||layout.Y is <0 or >1||!double.IsFinite(layout.Width)||layout.Width is <200 or >2000||!double.IsFinite(layout.Height)||layout.Height is <100 or >1600)throw new ArgumentException("Invalid layout geometry");
        int width=Math.Clamp((int)Math.Round(layout.Width*dpi/96),1,work.Width),height=Math.Clamp((int)Math.Round((layout.Folded?160:layout.Height)*dpi/96),1,work.Height);
        return new(work.Left+Math.Min((int)Math.Round(layout.X*work.Width),work.Width-width),work.Top+Math.Min((int)Math.Round(layout.Y*work.Height),work.Height-height),width,height);
    }
    private static void Check(PixelWorkArea work)
    {
        if(work.Width<1||work.Height<1||(long)work.Left+work.Width>int.MaxValue||(long)work.Top+work.Height>int.MaxValue)throw new ArgumentException("Invalid work area");
    }
}

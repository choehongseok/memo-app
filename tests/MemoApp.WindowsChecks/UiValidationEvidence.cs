using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MemoApp.Windows;

internal static class UiValidationEvidence
{
    internal static void Capture(Window window,string name)
    {
        string? requested=Environment.GetEnvironmentVariable("MEMO_UI_EVIDENCE");
        if(string.IsNullOrEmpty(requested))return;
        if(!Path.IsPathFullyQualified(requested)||name.Any(character=>!char.IsAsciiLetterOrDigit(character)&&character!='-'))
            throw new IOException("Invalid synthetic UI evidence destination/name");
        if(window.Content is not FrameworkElement content||!window.IsVisible||!content.IsLoaded||content.ActualWidth<=0||content.ActualHeight<=0)
            throw new IOException("Evidence requires a real loaded visible synthetic window");
        // Only this synthetic test's own live WPF client visual. No desktop, other windows, input injection or secret fields.
        var dpi=VisualTreeHelper.GetDpi(content);
        int width=checked((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX));
        int height=checked((int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY));
        if(width<1||height<1||width>4096||height>4096)throw new IOException("Synthetic capture bounds");
        string root=Path.GetFullPath(requested);Directory.CreateDirectory(root);
        string path=Path.Combine(root,name+".png");
        var raster=new RenderTargetBitmap(width,height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        // HwndTarget normally composites Window.Background separately. Preserve that actual brush
        // and render from the Window origin so the content margin cannot crop its bottom/right edge.
        var background=new DrawingVisual();
        using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background??SystemColors.WindowBrush,null,new Rect(0,0,window.ActualWidth,window.ActualHeight));
        raster.Render(background);raster.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(raster));
        using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))encoder.Save(output);
        using var encoded=File.OpenRead(path);
        string hash=Convert.ToHexStringLower(SHA256.HashData(encoded));
        string assembly=typeof(MainWindow).Assembly.Location;
        using var binary=File.OpenRead(assembly);
        var metadata=new{captureKind="live-native-WPF-window-visual-render with Window.Background; OS chrome/desktop/physical input not captured",width,height,dpi=dpi.PixelsPerInchX,windowScale=window.LayoutTransform.Value.M11,pngSha256=hash,productAssemblySha256=Convert.ToHexStringLower(SHA256.HashData(binary)),timeUtc=DateTimeOffset.UtcNow};
        using var manifest=new FileStream(Path.Combine(root,name+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(manifest,metadata);
        Console.WriteLine("EVIDENCE: synthetic WPF client "+name+" "+width+"x"+height);
    }
}

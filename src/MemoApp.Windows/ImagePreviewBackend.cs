using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;

// Separate pixel decoder/display adapter. No source note/session or UI publication authority.
internal class ImagePreviewBackend
{
    internal virtual OwnedBgraRaster Decode(AttachmentReadLease lease,CancellationToken token)=>AttachmentPngPreview.Decode(lease,token);
    internal virtual BitmapSource Create(OwnedBgraRaster raster)
    {
        BitmapSource? bitmap=null;
        if(!raster.ConsumePixels(pixels=>
        {
            byte[] copy=pixels.ToArray();
            try{bitmap=BitmapSource.Create(raster.Width,raster.Height,96,96,PixelFormats.Bgra32,null,copy,raster.Stride);bitmap.Freeze();}
            finally{CryptographicOperations.ZeroMemory(copy);}
        }))throw new OperationCanceledException();
        return bitmap??throw new InvalidOperationException("No detached bitmap");
    }
}

internal interface IImageDisplayHost
{
    void InvalidateImageDisplay();
}

internal static class ImagePreviewAdmission
{
    private static readonly object gate=new();
    private static Dispatcher? dispatcher;
    private static bool occupied;
    private static IImageDisplayHost? displayed;
    internal static bool TryEnter(Dispatcher context)
    {
        context.VerifyAccess();
        lock(gate)
        {
            if(dispatcher is not null&&!ReferenceEquals(dispatcher,context))throw new InvalidOperationException("Preview requires the application UI Dispatcher");
            if(occupied||context.HasShutdownStarted)return false;
            dispatcher=context;occupied=true;return true;
        }
    }
    internal static void Exit(){lock(gate){occupied=false;}}
    internal static void ClearDisplayed()
    {
        dispatcher?.VerifyAccess();var old=displayed;displayed=null;old?.InvalidateImageDisplay();
    }
    internal static void PublishHost(IImageDisplayHost panel)
    {dispatcher?.VerifyAccess();displayed=panel;}
    internal static void Forget(IImageDisplayHost panel)
    {dispatcher?.VerifyAccess();if(ReferenceEquals(displayed,panel))displayed=null;}
}

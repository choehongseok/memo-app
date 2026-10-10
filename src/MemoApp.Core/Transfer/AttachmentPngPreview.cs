using MemoApp.Core.Editing;
namespace MemoApp.Core.Transfer;

// Only a registered authenticated single-use lease enters this decoder bridge.
// The caller must admit application-wide work first and separately authorize UI publication.
public static class AttachmentPngPreview
{
    public static OwnedBgraRaster Decode(AttachmentReadLease lease,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        OwnedBgraRaster? raster=null;bool returned=false;
        try
        {
            if(!lease.Consume(bytes=>raster=PngPixelDecoder.Decode(bytes,cancellation)))throw new OperationCanceledException();
            cancellation.ThrowIfCancellationRequested();
            returned=true;return raster??throw new InvalidOperationException("No detached preview raster");
        }
        finally{if(!returned)raster?.Dispose();lease.Dispose();}
    }
}

using System.Security.Cryptography;
using MemoApp.Core.Editing;
namespace MemoApp.Core.Transfer;

// One authenticated lease read, same-span digest/header/decode. No issuer, note, key or UI owner retained.
internal sealed class AttachmentOcrInput : IDisposable
{
    private readonly object gate=new();
    private OwnedBgraRaster? raster;
    private bool disposed,taken;
    internal OcrSourceDescriptor Source{get;}
    internal OcrGrantStamp Stamp{get;}
    internal int SourceWidth{get;}
    internal int SourceHeight{get;}
    internal int PreviewWidth{get;}
    internal int PreviewHeight{get;}
    private AttachmentOcrInput(OcrSourceDescriptor source,OcrGrantStamp stamp,PngPreviewHeader header,OwnedBgraRaster pixels)
    {Source=source;Stamp=stamp;SourceWidth=header.Width;SourceHeight=header.Height;PreviewWidth=pixels.Width;PreviewHeight=pixels.Height;raster=pixels;}
    internal static AttachmentOcrInput Capture(AttachmentReadLease lease,OcrSourceDescriptor source,OcrGrantStamp stamp,CancellationToken token=default,Action<byte[]>? allocations=null)
    {
        OwnedBgraRaster? pixels=null;PngPreviewHeader? header=null;
        try
        {
            ArgumentNullException.ThrowIfNull(lease);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(stamp);
            token.ThrowIfCancellationRequested();source.Validate();stamp.Validate();
            bool consumed=lease.Consume(bytes=>
            {
                token.ThrowIfCancellationRequested();if(bytes.Length!=source.Length)throw new InvalidDataException("OCR source length");
                Span<byte> expected=stackalloc byte[32],actual=stackalloc byte[32];
                try
                {
                    for(int i=0;i<32;i++)expected[i]=(byte)((Hex(source.Sha256[i*2])<<4)|Hex(source.Sha256[i*2+1]));
                    SHA256.HashData(bytes,actual);token.ThrowIfCancellationRequested();if(!CryptographicOperations.FixedTimeEquals(actual,expected))throw new InvalidDataException("OCR source hash");
                    header=PngPreviewProfile.Inspect(bytes);pixels=PngPixelDecoder.Decode(bytes,token,allocations);
                }
                finally{expected.Clear();actual.Clear();}
            });
            if(!consumed)throw new OperationCanceledException();token.ThrowIfCancellationRequested();
            var result=new AttachmentOcrInput(source,stamp,header!,pixels!);pixels=null;return result;
        }
        finally{pixels?.Dispose();lease?.Dispose();}
    }
    private static int Hex(char value)=>value<='9'?value-'0':value-'a'+10;
    internal OwnedBgraRaster TakeRaster()
    {lock(gate){if(disposed)throw new ObjectDisposedException(nameof(AttachmentOcrInput));if(taken)throw new InvalidOperationException("OCR raster ownership already transferred");taken=true;var result=raster!;raster=null;return result;}}
    public void Dispose(){OwnedBgraRaster? old;lock(gate){if(disposed)return;disposed=true;old=raster;raster=null;}old?.Dispose();}
}

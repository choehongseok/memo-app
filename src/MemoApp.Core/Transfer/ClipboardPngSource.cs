using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;
// A bounded owned opaque PNG copy. Structure validation does not prove pixels or grant publication authority.
public sealed class ClipboardPngSource : IDisposable
{
    private byte[] bytes;
    private bool disposed;
    private ClipboardPngSource(byte[] bytes)=>this.bytes=bytes;
    // Converter has validated the complete PNG; ownership transfers without another plaintext copy.
    internal static ClipboardPngSource TakeValidatedOwnership(byte[] owned) => new(owned);
    public ReadOnlySpan<byte> Content=>disposed?throw new ObjectDisposedException(nameof(ClipboardPngSource)):bytes;
    public static ClipboardPngSource Capture(byte[] borrowed)
    {
        ArgumentNullException.ThrowIfNull(borrowed);if(borrowed.Length is <8 or >4194304)throw new InvalidDataException("Clipboard PNG size limit");byte[] copy=(byte[])borrowed.Clone();
        try{_ = PngPreviewProfile.Inspect(copy);return new(copy);}catch{CryptographicOperations.ZeroMemory(copy);throw;}
    }
    public void Dispose(){if(disposed)return;disposed=true;CryptographicOperations.ZeroMemory(bytes);bytes=[];}
}

using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;
internal static class BoundedFileReader
{
    // Borrow the stream only. Capture Length once so allocation cannot outrun the validated scalar.
    internal static byte[] Read(Stream input,int minimum,int maximum,CancellationToken token)
    {
        if(minimum<0||maximum<minimum)throw new ArgumentOutOfRangeException(nameof(maximum));token.ThrowIfCancellationRequested();long length=input.Length;
        if(length<minimum||length>maximum)throw new InvalidDataException("Source file byte limit");byte[] bytes=new byte[checked((int)length)];
        try
        {
            int offset=0;while(offset<bytes.Length){token.ThrowIfCancellationRequested();int read=input.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));if(read==0)throw new InvalidDataException("Short source file");offset+=read;}
            if(input.ReadByte()!=-1)throw new InvalidDataException("Source file changed while reading");token.ThrowIfCancellationRequested();return bytes;
        }
        catch{CryptographicOperations.ZeroMemory(bytes);throw;}
    }
}

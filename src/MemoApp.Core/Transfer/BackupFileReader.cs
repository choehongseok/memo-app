using System.Security.Cryptography;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;
// Returns an owned bounded ciphertext buffer. Caller must zero it after read-only authentication or discard.
public static class BackupFileReader
{
    public static byte[] Read(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();path=LocalFilePath.Resolve(path);LocalFilePath.CheckAncestors(path,true);using var stream=LocalRegularFile.Open(path);if(stream.Length is <1 or >VaultEnvelope.MaxFile)throw new InvalidDataException("Backup source file limit");byte[] bytes=new byte[checked((int)stream.Length)];
        try{int offset=0;while(offset<bytes.Length){token.ThrowIfCancellationRequested();int read=stream.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));if(read==0)throw new InvalidDataException("Short backup source");offset+=read;}if(stream.ReadByte()!=-1)throw new InvalidDataException("Changed backup source");token.ThrowIfCancellationRequested();return bytes;}
        catch{CryptographicOperations.ZeroMemory(bytes);throw;}
    }
}

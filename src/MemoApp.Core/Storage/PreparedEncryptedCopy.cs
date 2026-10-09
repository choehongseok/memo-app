using System.Security.Cryptography;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Storage;
// Detached authenticated ciphertext; contains no recovery/root/content key or workspace graph.
public sealed class PreparedEncryptedCopy:IDisposable
{
    private byte[] bytes;
    private readonly byte[] digest;
    private readonly string sourceRoot;
    private bool disposed;
    internal PreparedEncryptedCopy(byte[] bytes,string sourceRoot){this.bytes=bytes;digest=SHA256.HashData(bytes);this.sourceRoot=sourceRoot;}
    public void WriteTo(string destination,IAtomicVaultFiles? files=null)
    {
        if(disposed)throw new InvalidOperationException("Disposed encrypted copy");destination=LocalFilePath.Resolve(destination);LocalFilePath.CheckAncestors(destination,false);
        string relative=Path.GetRelativePath(sourceRoot,destination);if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Backup inside active vault refused");
        var adapter=files??new AtomicVaultFiles();using(var output=adapter.CreateNew(destination)){output.Write(bytes);adapter.FlushToDisk(output);}
        LocalFilePath.CheckAncestors(destination,true);using var input=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length!=bytes.Length)throw new IOException("Encrypted backup size changed");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] chunk=new byte[65536];
        try
        {
            int remaining=bytes.Length;while(remaining>0){int count=input.Read(chunk,0,Math.Min(chunk.Length,remaining));if(count==0)throw new IOException("Short encrypted backup");hash.AppendData(chunk,0,count);remaining-=count;}
            if(input.ReadByte()!=-1||input.Length!=bytes.Length||!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),digest))throw new IOException("Encrypted backup outcome uncertain");
        }
        finally{CryptographicOperations.ZeroMemory(chunk);}
    }
    public void Dispose(){if(disposed)return;disposed=true;CryptographicOperations.ZeroMemory(bytes);bytes=[];CryptographicOperations.ZeroMemory(digest);}
}

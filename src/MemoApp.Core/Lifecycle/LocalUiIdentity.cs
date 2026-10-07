using System.Security.Cryptography;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Lifecycle;
// Non-secret UI-profile locator. Never an authentication or vault-access credential.
public static class LocalUiIdentity
{
    private static readonly byte[] Magic="MEMOUI01"u8.ToArray();
    public static Guid GetOrCreate(string identityRoot)
    {
        string path=LocalFilePath.Resolve(Path.Combine(identityRoot,"ui-device.id"));
        identityRoot=Path.GetDirectoryName(path)!;LocalFilePath.CheckDataRoot(identityRoot);
        if(!OperatingSystem.IsWindows())Directory.CreateDirectory(identityRoot,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);else Directory.CreateDirectory(identityRoot);
        LocalFilePath.CheckDataRoot(identityRoot);var existing=Read(path);if(existing is Guid known)return known;
        var id=Guid.NewGuid();var bytes=new byte[32];Magic.CopyTo(bytes,0);id.ToByteArray(true).CopyTo(bytes,8);SHA256.HashData(bytes.AsSpan(0,24)).AsSpan(0,8).CopyTo(bytes.AsSpan(24));
        string temporary=Path.Combine(identityRoot,"pending-ui-"+Guid.NewGuid().ToString("N")+".id");bool owned=false;
        try
        {
            var files=new AtomicVaultFiles();using(var output=files.CreateNew(temporary)){owned=true;output.Write(bytes);files.FlushToDisk(output);}
            LocalFilePath.CheckDataRoot(identityRoot);
            try{File.Move(temporary,path,false);owned=false;}
            catch(IOException){if(Read(path) is not Guid)throw;}
            return Read(path)??throw new IOException("UI profile creation uncertain");
        }
        finally{if(owned)File.Delete(temporary);}
    }
    private static Guid? Read(string path)
    {
        FileAttributes attributes;
        try{attributes=File.GetAttributes(path);}catch(Exception e)when(e is FileNotFoundException or DirectoryNotFoundException){return null;}
        if((attributes&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw new IOException("Linked/non-file UI profile refused");
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length!=32)throw new InvalidDataException("UI profile format/size invalid");
        var bytes=new byte[32];input.ReadExactly(bytes);if(input.ReadByte()!=-1||!bytes.AsSpan(0,8).SequenceEqual(Magic)||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0,24)).AsSpan(0,8),bytes.AsSpan(24,8)))throw new InvalidDataException("UI profile integrity invalid");
        var id=new Guid(bytes.AsSpan(8,16),true);if(id==Guid.Empty)throw new InvalidDataException("Empty UI profile");return id;
    }
}

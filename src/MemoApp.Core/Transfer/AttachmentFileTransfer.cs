using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;
// Explicit plaintext copy only. It grants no authority to launch an external program.
public static class AttachmentFileTransfer
{
    public static string ResolveDestination(string destination,string protectedRoot)
    {
        destination=LocalFilePath.Resolve(destination);protectedRoot=LocalFilePath.Resolve(protectedRoot);LocalFilePath.CheckAncestors(destination,false);
        string relative=Path.GetRelativePath(protectedRoot,destination);
        if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext attachment inside active vault refused");
        return destination;
    }
    public static string ValidateLaunchFile(string destination,string protectedRoot)
    {destination=ResolveDestination(destination,protectedRoot);LocalFilePath.CheckAncestors(destination,true);return destination;}
    public static bool Write(AttachmentReadLease lease,string destination,string protectedRoot,CancellationToken token=default,IAtomicVaultFiles? files=null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            token.ThrowIfCancellationRequested();destination=ResolveDestination(destination,protectedRoot);var outputFiles=files??new AtomicVaultFiles();
            return lease.Consume(bytes=>
            {
                if(bytes.Length>4*1024*1024)throw new InvalidDataException("Attachment plaintext size limit");
                token.ThrowIfCancellationRequested();using var output=outputFiles.CreateNew(destination);token.ThrowIfCancellationRequested();
                for(int offset=0;offset<bytes.Length;offset+=65536){token.ThrowIfCancellationRequested();output.Write(bytes.Slice(offset,Math.Min(65536,bytes.Length-offset)));}
                token.ThrowIfCancellationRequested();outputFiles.FlushToDisk(output);token.ThrowIfCancellationRequested();
            });
        }
        finally{lease.Dispose();} // Includes cancellation/preflight failures before Consume.
        // Any owned empty/partial plaintext remains for the user; never unlink an unverified path.
    }
}

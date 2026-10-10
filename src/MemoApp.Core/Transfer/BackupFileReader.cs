using MemoApp.Core.Storage;
namespace MemoApp.Core.Transfer;
// Returns an owned bounded ciphertext buffer. Caller must zero it after read-only authentication or discard.
public static class BackupFileReader
{
    public static byte[] Read(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();path=LocalFilePath.Resolve(path);LocalFilePath.CheckAncestors(path,true);using var stream=LocalRegularFile.Open(path);
        return BoundedFileReader.Read(stream,1,VaultEnvelope.MaxFile,token);
    }
}

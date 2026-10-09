using System.Globalization;
using System.Security.Cryptography;
namespace MemoApp.Core.Storage;
public sealed record BackupNotePreview(Guid NoteId,string Title,string Excerpt,bool Truncated,bool Deleted,int AttachmentCount);
public sealed record EncryptedBackupPreview(ulong Sequence,int HistoryCount,int FolderCount,int TagCount,BackupNotePreview[] Notes);
public sealed partial class EncryptedVault
{
    internal EncryptedBackupPreview PreviewEncryptedBackup(byte[] borrowed)
    {
        ArgumentNullException.ThrowIfNull(borrowed);if(borrowed.Length is <1 or >VaultEnvelope.MaxFile)throw new InvalidDataException("Backup preview file limit");byte[] copy=(byte[])borrowed.Clone();
        try
        {
            lock(gate)
            {
                if(disposed||keysReleased||faulted)throw new InvalidOperationException("Backup preview authority ended");EncryptedBackupPreview result;
                using(var decoded=VaultEnvelope.DecryptOwned(copy,recoveryKey))
                {
                    if(decoded.Header.VaultId!=vaultId)throw new InvalidDataException("Backup belongs to another vault");var snapshot=decoded.Snapshot;
                    result=new(decoded.Header.Sequence,snapshot.History.Length,snapshot.Folders.Length,snapshot.Tags.Length,snapshot.Notes.Select(n=>new BackupNotePreview(n.NoteId,n.Title,Excerpt(n.Text),n.Text.Length>256,n.Metadata.Deleted,n.AttachmentIds.Length)).ToArray());
                }
                if(disposed||keysReleased||faulted)throw new InvalidOperationException("Backup preview authority ended");return result;
            }
        }
        finally{CryptographicOperations.ZeroMemory(copy);}
    }
    internal VaultSnapshot AuthenticateBackupSnapshot(byte[] borrowed)
    {
        ArgumentNullException.ThrowIfNull(borrowed);if(borrowed.Length is <1 or >VaultEnvelope.MaxFile)throw new InvalidDataException("Backup source file limit");byte[] copy=(byte[])borrowed.Clone();
        try{lock(gate){if(disposed||keysReleased||faulted)throw new InvalidOperationException("Backup import authority ended");using var decoded=VaultEnvelope.DecryptOwned(copy,recoveryKey);if(decoded.Header.VaultId!=vaultId)throw new InvalidDataException("Backup belongs to another vault");return decoded.Snapshot;}}
        finally{CryptographicOperations.ZeroMemory(copy);}
    }
    internal void ValidateImportedCandidate(VaultSnapshot candidate)
    {
        lock(gate)
        {
            if(disposed||keysReleased||faulted)throw new InvalidOperationException("Backup import authority ended");VaultEnvelope.Validate(candidate);
            if(candidate.SchemaVersion<5){if(attachmentRootId!=Guid.Empty)throw new InvalidOperationException("Imported candidate cannot discard current root");}
            else RequireRootSnapshot(candidate); // Actual current-root authentication; no encryption/write/counter transition.
        }
    }
    private static string Excerpt(string source)
    {
        if(source.Length<=256)return source;int end=0;foreach(int boundary in StringInfo.ParseCombiningCharacters(source)){if(boundary>255)break;end=boundary;}return source[..end]+"…";
    }
}

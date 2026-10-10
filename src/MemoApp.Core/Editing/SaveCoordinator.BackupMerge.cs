using System.Collections.Immutable;
using System.Security.Cryptography;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Editing;

// Host capability is owner-only. Workers receive only a path/token or detached ciphertext copy.
public sealed class BackupMergeViewAuthority:IDisposable
{
    internal readonly SaveCoordinator Owner;
    private Func<bool>? current;
    private readonly CancellationTokenSource cancellation=new();
    private readonly CancellationToken cancellationToken;
    internal BackupMergePreviewToken? Displayed;
    internal bool Closed=>current is null;
    internal CancellationToken Cancellation=>cancellationToken;
    internal BackupMergeViewAuthority(SaveCoordinator owner,Func<bool> current){Owner=owner;this.current=current;cancellationToken=cancellation.Token;}
    internal bool IsCurrent()=>current?.Invoke()==true&&!Closed;
    public void MarkDisplayed(BackupMergePreviewToken token)
    {
        Owner.RequireMergePreview(token);if(!ReferenceEquals(token.View,this))throw new InvalidOperationException("Different merge view");Displayed=token;
    }
    public void Dispose()
    {Owner.EnsureMergeOwner();if(Closed)return;current=null;Displayed=null;Owner.RevokeMergeView(this);try{cancellation.Cancel();}finally{cancellation.Dispose();}}
}
public sealed class BackupMergePreviewToken
{
    internal readonly BackupMergeViewAuthority View;
    internal readonly long Epoch,Generation,PreviewEpoch;
    internal readonly string Source,Hash;
    internal PreparedBackupMerge? Prepared;
    internal readonly ImmutableArray<Guid> Selected;
    internal bool Confirmed,Consumed,OwnRevocation;
    internal BackupMergePreviewToken(BackupMergeViewAuthority view,long epoch,long generation,long previewEpoch,string source,string hash,PreparedBackupMerge prepared,Guid[] selected)
    {View=view;Epoch=epoch;Generation=generation;PreviewEpoch=previewEpoch;Source=source;Hash=hash;Prepared=prepared;Selected=selected.ToImmutableArray();}
}
public sealed class ConfirmedBackupMergeToken
{
    internal readonly BackupMergePreviewToken Preview;
    internal ConfirmedBackupMergeToken(BackupMergePreviewToken preview){Preview=preview;}
}
public sealed record BackupMergePreview(BackupMergePreviewToken Token,int ImportedRevisionCount,ImmutableArray<Guid> SelectedIds,ImmutableArray<BackupMergePendingBranch> PendingBranches,ImmutableArray<BackupMergeBranchComparison> BranchDetails);

public sealed partial class SaveCoordinator
{
    private Func<bool>? mergeCheckAccess;
    private BackupMergePreviewToken? mergePreview,mergeApplyPermit;
    private bool mergeBusy;
    // Trusted platform policy: WPF passes Dispatcher.CheckAccess, never a context-instance comparison.
    public BackupMergeViewAuthority CreateBackupMergeView(Func<bool> current,Func<bool> checkAccess)
    {
        ArgumentNullException.ThrowIfNull(current);ArgumentNullException.ThrowIfNull(checkAccess);
        if(mergeCheckAccess is null){if(!checkAccess())throw new InvalidOperationException("Merge owner access required");mergeCheckAccess=checkAccess;}
        EnsureMergeOwner();if(!checkAccess())throw new InvalidOperationException("Merge view owner access required");RequireMergeOpen();return new(this,current);
    }
    internal void EnsureMergeOwner()
    {if(mergeCheckAccess?.Invoke()!=true)throw new InvalidOperationException("Merge owner access required");}
    private void RequireMergeOpen()
    {EnsureMergeOwner();if(disposed||IsLocked||vault.IsFaulted)throw new InvalidOperationException("Merge authority ended");}
    internal void RevokeMergeView(BackupMergeViewAuthority view)
    {EnsureMergeOwner();if(mergePreview is { } token&&ReferenceEquals(token.View,view)){token.Prepared=null;mergePreview=null;}}
    private static byte[] OwnMergeCipher(byte[] cipher)
    {ArgumentNullException.ThrowIfNull(cipher);if(cipher.Length is <1 or >VaultEnvelope.MaxFile)throw new InvalidDataException("Merge source file bound");return(byte[])cipher.Clone();}
    private void CheckMergeTokenState(BackupMergePreviewToken token,bool applying)
    {
        RequireMergeOpen();var prepared=token.Prepared;
        long expectedEpoch=token.PreviewEpoch+(applying&&token.OwnRevocation?1:0);
        if(prepared is null||!ReferenceEquals(mergePreview,token)||!ReferenceEquals(token.View.Owner,this)||token.View.Closed||token.Epoch!=sessionEpoch||token.Generation!=generation||expectedEpoch!=AttachmentPreviewEpoch||IsDirty||!Workspace.IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint))throw new InvalidOperationException("Merge preview changed");
    }
    internal void RequireMergePreview(BackupMergePreviewToken token,bool applying=false)
    {
        ArgumentNullException.ThrowIfNull(token);CheckMergeTokenState(token,applying);
        if(!token.View.IsCurrent())throw new InvalidOperationException("Merge view changed");
        // IsCurrent is a native-host callback: never trust state tested before invoking it.
        CheckMergeTokenState(token,applying);
    }
    public ConfirmedBackupMergeToken ConfirmBackupMerge(BackupMergePreviewToken token)
    {
        if(mergeBusy)throw new InvalidOperationException("Merge operation occupied");RequireMergePreview(token);
        if(token.Consumed||token.Confirmed||!ReferenceEquals(token.View.Displayed,token))throw new InvalidOperationException("Exact displayed merge preview required");
        token.Confirmed=true;return new(token);
    }
    public async Task<BackupMergePreview> PreviewEncryptedBackupMergeAsync(byte[] cipher,Guid[] selected,string sourcePath,long expectedPreviewEpoch,BackupMergeViewAuthority view)
    {
        EnsureMergeOwner(); // The trusted access predicate must be side-effect-free.
        byte[] owned=OwnMergeCipher(cipher);Guid[] ids;
        try{ArgumentNullException.ThrowIfNull(selected);if(selected.Length is <1 or >100||selected.Any(id=>id==Guid.Empty)||selected.Distinct().Count()!=selected.Length)throw new ArgumentException("Merge selection bounds");ids=(Guid[])selected.Clone();}
        catch{CryptographicOperations.ZeroMemory(owned);throw;}
        bool admitted=false,returned=false;BackupMergePreviewToken? created=null;
        try
        {
            RequireMergeOpen();ArgumentNullException.ThrowIfNull(view);if(mergeBusy||IsBusy||!ReferenceEquals(view.Owner,this))throw new InvalidOperationException("Merge preview occupied or wrong view");
            mergeBusy=true;admitted=true;if(mergePreview is { } old){old.Prepared=null;old.View.Displayed=null;mergePreview=null;}
            var workspace=Workspace;long epoch=sessionEpoch,startGeneration=generation,startPreview=AttachmentPreviewEpoch;
            var versions=workspace.Notes.Select(n=>(Note:n,Version:n.EditVersion)).ToArray();
            void CurrentRequest(long allowedPreview)
            {
                RequireMergeOpen();if(!ReferenceEquals(Workspace,workspace)||epoch!=sessionEpoch||generation!=startGeneration||AttachmentPreviewEpoch!=allowedPreview||view.Closed||versions.Any(v=>v.Note.IsClosed||v.Note.EditVersion!=v.Version))throw new InvalidOperationException("Merge request changed");
                if(!view.IsCurrent())throw new InvalidOperationException("Merge view closed");
                RequireMergeOpen();if(!ReferenceEquals(Workspace,workspace)||epoch!=sessionEpoch||generation!=startGeneration||AttachmentPreviewEpoch!=allowedPreview||view.Closed||versions.Any(v=>v.Note.IsClosed||v.Note.EditVersion!=v.Version))throw new InvalidOperationException("Merge request changed during callback");
            }
            if(startPreview!=expectedPreviewEpoch)throw new InvalidOperationException("Stale merge request");CurrentRequest(startPreview);
            string path=LocalFilePath.Resolve(sourcePath);LocalFilePath.CheckAncestors(path,true);string hash=Convert.ToHexStringLower(SHA256.HashData(owned));
            long settledPreview=startPreview;
            if(IsDirty)
            {
                if(!await SaveAsync()||IsDirty)throw new InvalidOperationException("Merge checkpoint failed");
                settledPreview=startPreview+1;CurrentRequest(settledPreview); // Exactly this SaveAsync's single AcceptPrepared revocation is expected.
            }
            else CurrentRequest(startPreview);
            if(IsDirty||IsBusy)throw new InvalidOperationException("Merge requires clean committed state");
            // Authenticate and hash one owned input, then check actual current-root key authority.
            var backup=vault.AuthenticateBackupSnapshot(owned);
            var prepared=Workspace.PrepareBackupMerge(backup,ids,candidate=>
            {
                if(candidate.AttachmentObjects.Length>0&&!vault.AttachmentRootAnchored)throw new InvalidOperationException("Anchored current attachment root required");
                vault.ValidateImportedCandidate(candidate);
            });
            CurrentRequest(settledPreview);if(IsDirty)throw new InvalidOperationException("Merge became dirty");
            var token=new BackupMergePreviewToken(view,sessionEpoch,generation,AttachmentPreviewEpoch,path,hash,prepared,ids);created=token;mergePreview=token;
            var details=EditingWorkspace.BranchDetails(prepared.Candidate,prepared.Pending);
            RequireMergePreview(token);returned=true;return new(token,prepared.Imported,token.Selected,prepared.Pending,details);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(owned);
            if(!returned&&created is { } failed){failed.Prepared=null;if(ReferenceEquals(failed.View.Displayed,failed))failed.View.Displayed=null;if(ReferenceEquals(mergePreview,failed))mergePreview=null;}
            if(admitted)mergeBusy=false;
        }
    }
    public Task<BackupMergeResult> MergeEncryptedBackupAsync(ConfirmedBackupMergeToken confirmed,byte[] rereadCipher)
        =>StartBackupMerge(confirmed,rereadCipher,null);
    internal Task<BackupMergeResult> StartBackupMerge(ConfirmedBackupMergeToken confirmed,byte[] rereadCipher,IAtomicVaultFiles? recoveryFiles)
    {
        EnsureMergeOwner();
        var owned=OwnMergeCipher(rereadCipher);
        try
        {
            RequireMergeOpen();ArgumentNullException.ThrowIfNull(confirmed);if(mergeBusy||IsBusy)throw new InvalidOperationException("Merge operation occupied");
            var token=confirmed.Preview;RequireMergePreview(token);
            if(!token.Confirmed||token.Consumed||!ReferenceEquals(token.View.Displayed,token))throw new InvalidOperationException("Confirmed merge token ended");
            token.Consumed=true;
            if(token.Hash!=Convert.ToHexStringLower(SHA256.HashData(owned)))throw new InvalidDataException("Confirmed merge source changed");
            _=vault.AuthenticateBackupSnapshot(owned);RequireMergePreview(token);
            mergeBusy=true;var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);backupTask=completion.Task;
            return CompleteBackupMergeAsync(token,owned,recoveryFiles,completion);
        }
        catch{CryptographicOperations.ZeroMemory(owned);throw;}
    }
    // The one expected revocation is granted only to our own trusted invalidation handler.
    private void ObserveMergeRevocation(NoteDraft? source)
    {
        if(mergeApplyPermit is {OwnRevocation:false} token&&Workspace.IsMergePublicationActive&&source is null&&token.PreviewEpoch+1==AttachmentPreviewEpoch&&token.Generation==generation&&token.Epoch==sessionEpoch)
        {token.OwnRevocation=true;return;}
        if(mergePreview is { } stale){stale.Prepared=null;stale.View.Displayed=null;mergePreview=null;}
    }
    private async Task<BackupMergeResult> CompleteBackupMergeAsync(BackupMergePreviewToken token,byte[] owned,IAtomicVaultFiles? recoveryFiles,TaskCompletionSource<bool> completion)
    {
        bool applied=false;var prepared=token.Prepared!;
        try
        {
            RequireMergePreview(token);
            if(prepared.Imported==0)return new(BackupMergeDisposition.NoChange,0,prepared.Pending);
            using(var copy=vault.CaptureCommittedCopy())
            {
                string destination=Path.Combine(vault.DataRoot,$"previous-{Guid.NewGuid():N}.vault");
                await WriteMergeRecoveryAsync(copy,destination,recoveryFiles);
            }
            RequireMergePreview(token);
            byte[] last=await ReadMergeSourceAsync(token.Source,token.View.Cancellation);
            try
            {
                RequireMergePreview(token);if(token.Hash!=Convert.ToHexStringLower(SHA256.HashData(last)))throw new InvalidDataException("Merge source changed during preservation");
                _=vault.AuthenticateBackupSnapshot(last);RequireMergePreview(token);
            }
            finally{CryptographicOperations.ZeroMemory(last);}
            vault.ValidateImportedCandidate(prepared.Candidate);RequireMergePreview(token);
            mergeApplyPermit=token;bool observers;
            try
            {
                observers=Workspace.ApplyPreparedBackupMerge(prepared,()=>
                {RequireMergePreview(token,true);return true;},()=>applied=true);
            }
            finally{mergeApplyPermit=null;}
            if(!observers||disposed||IsLocked||token.Epoch!=sessionEpoch)return new(BackupMergeDisposition.AppliedDirty,prepared.Imported,prepared.Pending);
            bool saved=await SaveAsync();return new(saved&&!IsDirty&&!IsLocked?BackupMergeDisposition.AppliedAndSaved:BackupMergeDisposition.AppliedDirty,prepared.Imported,prepared.Pending);
        }
        catch{return new(applied?BackupMergeDisposition.AppliedDirty:BackupMergeDisposition.NotApplied,applied?prepared.Imported:0,applied?prepared.Pending:[]);}
        finally
        {
            CryptographicOperations.ZeroMemory(owned);token.Prepared=null;token.View.Displayed=null;if(ReferenceEquals(mergePreview,token))mergePreview=null;mergeBusy=false;completion.TrySetResult(true);
        }
    }
    private static Task WriteMergeRecoveryAsync(PreparedEncryptedCopy copy,string destination,IAtomicVaultFiles? files)=>Task.Run(()=>copy.WriteMergeRecovery(destination,files));
    private static Task<byte[]> ReadMergeSourceAsync(string path,CancellationToken cancellation)=>Task.Run(()=>BackupFileReader.Read(path,cancellation));
}

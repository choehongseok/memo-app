using System.Collections.Immutable;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

public sealed class BranchResolutionViewAuthority:IDisposable
{
    internal readonly SaveCoordinator Owner;
    private Func<bool>? current;
    private readonly CancellationTokenSource cancellation=new();
    private readonly CancellationToken token;
    internal BranchResolutionPreviewToken? Displayed;
    internal bool Closed=>current is null;
    internal CancellationToken Cancellation=>token;
    internal bool IsCheckpointActive=>Owner.IsBranchCheckpoint(this);
    internal BranchResolutionViewAuthority(SaveCoordinator owner,Func<bool> current){Owner=owner;this.current=current;token=cancellation.Token;}
    internal bool IsCurrent()=>current?.Invoke()==true&&!Closed;
    public void MarkDisplayed(BranchResolutionPreviewToken preview)
    {Owner.RequireBranchPreview(preview);if(!ReferenceEquals(preview.View,this))throw new InvalidOperationException("Different branch resolution host");Displayed=preview;}
    public void Dispose()
    {Owner.EnsureMergeOwner();if(Closed)return;current=null;Displayed=null;Owner.RevokeBranchView(this);try{cancellation.Cancel();}finally{cancellation.Dispose();}}
}
public sealed class BranchResolutionPreviewToken
{
    internal readonly BranchResolutionViewAuthority View;
    internal readonly Guid OwnerNonce,TokenId=Guid.NewGuid();
    internal readonly NoteDraft Note;
    internal readonly long SessionEpoch,Generation,PreviewEpoch,Version;
    internal PreparedBranchResolution? Prepared;
    internal bool Confirmed,Consumed,OwnRevocation;
    internal BranchResolutionPreviewToken(BranchResolutionViewAuthority view,Guid nonce,NoteDraft note,long sessionEpoch,long generation,long previewEpoch,PreparedBranchResolution prepared)
    {View=view;OwnerNonce=nonce;Note=note;Version=note.EditVersion;SessionEpoch=sessionEpoch;Generation=generation;PreviewEpoch=previewEpoch;Prepared=prepared;}
}
public sealed class ConfirmedBranchResolutionToken
{
    internal readonly BranchResolutionPreviewToken Preview;
    internal ConfirmedBranchResolutionToken(BranchResolutionPreviewToken preview){Preview=preview;}
}
public sealed record BranchResolutionPreview(BranchResolutionPreviewToken Token,Guid NoteId,Guid CurrentRevisionId,Guid SelectedTipRevisionId,BackupMergeBranchComparison Comparison,ImmutableArray<Guid> RemainingTips);
public enum BranchResolutionDisposition { NotApplied,NoChange,AppliedAndSaved,AppliedDirty }
public sealed record BranchResolutionResult(BranchResolutionDisposition Disposition,Guid? NewRevisionId,ImmutableArray<Guid> RemainingTips);

public sealed partial class SaveCoordinator
{
    private readonly Guid branchOwnerNonce=Guid.NewGuid();
    private BranchResolutionPreviewToken? branchPreview,branchApplyPermit;
    private BranchResolutionViewAuthority? branchCheckpoint;
    internal bool IsBranchCheckpoint(BranchResolutionViewAuthority view)=>ReferenceEquals(branchCheckpoint,view);
    public BranchResolutionViewAuthority CreateBranchResolutionView(Func<bool> current,Func<bool> checkAccess)
    {
        ArgumentNullException.ThrowIfNull(current);ArgumentNullException.ThrowIfNull(checkAccess);
        if(mergeCheckAccess is null){if(!checkAccess())throw new InvalidOperationException("Recovery owner access required");mergeCheckAccess=checkAccess;}
        RequireBranchOpen();if(!checkAccess())throw new InvalidOperationException("Branch host owner access required");return new(this,current);
    }
    private void RequireBranchOpen()
    {EnsureMergeOwner();if(disposed||IsLocked||vault.IsFaulted||vault.KeysReleased)throw new InvalidOperationException("Branch resolution session ended");}
    private void ReleaseBranchPreview(BranchResolutionPreviewToken token)
    {token.Prepared=null;if(ReferenceEquals(token.View.Displayed,token))token.View.Displayed=null;if(ReferenceEquals(branchPreview,token))branchPreview=null;}
    private void RevokeBranchResolutionPreview()
    {if(branchPreview is { } token){EnsureMergeOwner();ReleaseBranchPreview(token);}}
    private void RevokeOtherMergePreview()
    {if(mergePreview is { } token){token.Prepared=null;token.View.Displayed=null;mergePreview=null;}}
    internal void RevokeBranchView(BranchResolutionViewAuthority view)
    {EnsureMergeOwner();if(branchPreview is { } token&&ReferenceEquals(token.View,view))ReleaseBranchPreview(token);}
    private void CheckBranchState(BranchResolutionPreviewToken token,bool attempting,bool publishing)
    {
        RequireBranchOpen();var prepared=token.Prepared;
        long expected=token.PreviewEpoch+(publishing&&token.OwnRevocation?1:0);
        if(prepared is null||!ReferenceEquals(branchPreview,token)||!ReferenceEquals(token.View.Owner,this)||token.OwnerNonce!=branchOwnerNonce||token.View.Closed||token.TokenId==Guid.Empty||token.SessionEpoch!=sessionEpoch||token.Generation!=generation||expected!=AttachmentPreviewEpoch||IsDirty||(!attempting&&token.Consumed)||token.Note.IsClosed||token.Note.Id!=prepared.NoteId||token.Note.EditVersion!=token.Version||!Workspace.Notes.Any(n=>ReferenceEquals(n,token.Note))||!Workspace.IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint))throw new InvalidOperationException("Branch resolution preview changed");
    }
    internal void RequireBranchPreview(BranchResolutionPreviewToken token,bool attempting=false,bool publishing=false)
    {
        ArgumentNullException.ThrowIfNull(token);CheckBranchState(token,attempting,publishing);
        if(!token.View.IsCurrent())throw new InvalidOperationException("Branch resolution host changed");
        CheckBranchState(token,attempting,publishing);
    }
    public ConfirmedBranchResolutionToken ConfirmBranchResolution(BranchResolutionPreviewToken token)
    {
        if(mergeBusy||IsBusy)throw new InvalidOperationException("Recovery operation occupied");RequireBranchPreview(token);
        if(token.Confirmed||token.Consumed||!ReferenceEquals(token.View.Displayed,token))throw new InvalidOperationException("Exact displayed branch preview required");
        token.Confirmed=true;return new(token);
    }
    public async Task<BranchResolutionPreview> PreviewBranchResolutionAsync(Guid noteId,Guid pendingTipRevisionId,long expectedPreviewEpoch,BranchResolutionViewAuthority view)
    {
        RequireBranchOpen();ArgumentNullException.ThrowIfNull(view);
        if(noteId==Guid.Empty||pendingTipRevisionId==Guid.Empty||mergeBusy||IsBusy||!ReferenceEquals(view.Owner,this))throw new InvalidOperationException("Branch resolution preview occupied or invalid source");
        mergeBusy=true;bool returned=false;BranchResolutionPreviewToken? created=null;
        try
        {
            RevokeBranchResolutionPreview();RevokeOtherMergePreview();
            var workspace=Workspace;long epoch=sessionEpoch,startGeneration=generation,startPreview=AttachmentPreviewEpoch;
            var versions=workspace.Notes.Select(n=>(Note:n,Version:n.EditVersion)).ToArray();
            var note=versions.SingleOrDefault(v=>v.Note.Id==noteId).Note??throw new InvalidOperationException("Branch note missing");
            void Pure(long allowed)
            {
                RequireBranchOpen();if(!ReferenceEquals(workspace,Workspace)||epoch!=sessionEpoch||generation!=startGeneration||AttachmentPreviewEpoch!=allowed||view.Closed||Workspace.Notes.Count!=versions.Length||versions.Any(v=>v.Note.IsClosed||v.Note.EditVersion!=v.Version||!Workspace.Notes.Any(n=>ReferenceEquals(n,v.Note))))throw new InvalidOperationException("Branch resolution request changed");
            }
            void Current(long allowed){Pure(allowed);if(!view.IsCurrent())throw new InvalidOperationException("Branch host closed");Pure(allowed);}
            if(expectedPreviewEpoch!=startPreview)throw new InvalidOperationException("Branch source epoch changed");Current(startPreview);
            long settledPreview=startPreview;
            if(IsDirty)
            {
                branchCheckpoint=view;
                try{if(!await SaveAsync()||IsDirty)throw new InvalidOperationException("Branch checkpoint failed");}
                finally{branchCheckpoint=null;}
                settledPreview=startPreview+1;Current(settledPreview);
            }
            if(IsDirty||IsBusy)throw new InvalidOperationException("Branch resolution needs clean committed state");Current(settledPreview);
            using(var current=vault.CaptureCommittedCopy()){} // Verify the authenticated current ciphertext, never the former external backup.
            Current(settledPreview);
            var prepared=Workspace.PrepareBranchResolution(noteId,pendingTipRevisionId,candidate=>{Current(settledPreview);vault.ValidateImportedCandidate(candidate);Current(settledPreview);});
            Current(settledPreview);
            created=new(view,branchOwnerNonce,note,sessionEpoch,generation,AttachmentPreviewEpoch,prepared);branchPreview=created;
            var comparison=EditingWorkspace.BranchDetails(prepared.ExpectedBasis,[new(noteId,pendingTipRevisionId)]).Single();
            RequireBranchPreview(created);returned=true;return new(created,noteId,prepared.OldCurrentRevisionId,pendingTipRevisionId,comparison,prepared.RemainingTips);
        }
        finally{if(!returned&&created is { } failed)ReleaseBranchPreview(failed);branchCheckpoint=null;mergeBusy=false;}
    }
    public Task<BranchResolutionResult> ResolvePendingBranchAsync(ConfirmedBranchResolutionToken confirmed)=>StartBranchResolution(confirmed,null);
    internal Task<BranchResolutionResult> StartBranchResolution(ConfirmedBranchResolutionToken confirmed,IAtomicVaultFiles? recoveryFiles)
    {
        EnsureMergeOwner();ArgumentNullException.ThrowIfNull(confirmed);var token=confirmed.Preview;
        // Foreign owner has no right to mutate another session's authority; all own attempts are consumed first.
        if(!ReferenceEquals(token.View.Owner,this)||token.OwnerNonce!=branchOwnerNonce||token.Consumed)return Task.FromResult(new BranchResolutionResult(BranchResolutionDisposition.NotApplied,null,[]));
        token.Consumed=true;
        try
        {
            if(mergeBusy||IsBusy)throw new InvalidOperationException("Recovery operation occupied");
            RequireBranchPreview(token,true);
            if(!token.Confirmed||!ReferenceEquals(token.View.Displayed,token))throw new InvalidOperationException("Confirmed branch resolution token required");
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RevokeOtherMergePreview();mergeBusy=true;backupTask=completion.Task;
            return CompleteBranchResolutionAsync(token,recoveryFiles,completion);
        }
        catch{ReleaseBranchPreview(token);return Task.FromResult(new BranchResolutionResult(BranchResolutionDisposition.NotApplied,null,[]));}
    }
    private void ObserveBranchResolutionRevocation(NoteDraft? source)
    {
        if(branchApplyPermit is {OwnRevocation:false} token&&Workspace.IsBranchResolutionPublicationActive&&ReferenceEquals(source,token.Note)&&token.PreviewEpoch+1==AttachmentPreviewEpoch&&token.Generation==generation&&token.SessionEpoch==sessionEpoch)
        {token.OwnRevocation=true;return;}
        RevokeBranchResolutionPreview();
    }
    private async Task<BranchResolutionResult> CompleteBranchResolutionAsync(BranchResolutionPreviewToken token,IAtomicVaultFiles? recoveryFiles,TaskCompletionSource<bool> completion)
    {
        var prepared=token.Prepared!;bool applied=false;
        BranchResolutionResult Result(bool saved)=>new(saved?BranchResolutionDisposition.AppliedAndSaved:BranchResolutionDisposition.AppliedDirty,prepared.NewRevisionId,prepared.RemainingTips);
        try
        {
            RequireBranchPreview(token,true);
            using(var copy=vault.CaptureCommittedCopy())
            {
                string destination=Path.Combine(vault.DataRoot,$"previous-{Guid.NewGuid():N}.vault");
                await WriteMergeRecoveryAsync(copy,destination,recoveryFiles);
            }
            RequireBranchPreview(token,true);
            using(var check=vault.CaptureCommittedCopy()){} // Recheck exact current ciphertext after preservation settles.
            RequireBranchPreview(token,true);vault.ValidateImportedCandidate(prepared.Candidate);RequireBranchPreview(token,true);
            branchApplyPermit=token;bool observers;
            try{observers=Workspace.ApplyPreparedBranchResolution(prepared,()=>{RequireBranchPreview(token,true,true);return true;},()=>applied=true);}
            finally{branchApplyPermit=null;}
            if(!observers||disposed||IsLocked||token.SessionEpoch!=sessionEpoch)return Result(false);
            bool saved=await SaveAsync();return Result(saved&&!IsDirty&&!IsLocked&&!disposed&&token.SessionEpoch==sessionEpoch);
        }
        catch{return applied||prepared.Applied?Result(false):new(BranchResolutionDisposition.NotApplied,null,[]);}
        finally{ReleaseBranchPreview(token);mergeBusy=false;completion.TrySetResult(true);}
    }
}

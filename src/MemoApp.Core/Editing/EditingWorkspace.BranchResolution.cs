using System.Collections.Immutable;
using System.Security.Cryptography;
using MemoApp.Core.History;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// Owner-only prepared state, never a worker DTO or public content/provenance factory.
internal sealed class PreparedBranchResolution(
    EditingWorkspace owner,VaultSnapshot expectedBasis,byte[] fingerprint,VaultSnapshot candidate,
    NoteDraft note,(NoteDraft Note,long Version)[] membership,long authorityEpoch,
    Guid oldCurrentRevisionId,Guid selectedTipRevisionId,ImmutableArray<Guid> remainingTips)
{
    internal readonly EditingWorkspace Owner=owner;
    internal readonly VaultSnapshot ExpectedBasis=expectedBasis,Candidate=candidate;
    internal readonly byte[] Fingerprint=fingerprint;
    internal readonly NoteDraft Note=note;
    internal readonly (NoteDraft Note,long Version)[] Membership=membership;
    internal readonly long AuthorityEpoch=authorityEpoch;
    internal readonly Guid NoteId=note.Id,OldCurrentRevisionId=oldCurrentRevisionId,SelectedTipRevisionId=selectedTipRevisionId;
    internal readonly Guid NewRevisionId=candidate.Notes.Single(n=>n.NoteId==note.Id).RevisionId;
    internal readonly ImmutableArray<Guid> RemainingTips=remainingTips;
    // Applied is the callback-free marker: observer/marker delegate failure cannot conceal installation.
    internal bool Consumed,Applied;
}
public sealed partial class EditingWorkspace
{
    private bool branchResolutionPublishing;
    internal bool IsBranchResolutionPublicationActive=>branchResolutionPublishing;
    private bool IsResolutionCurrent(PreparedBranchResolution prepared)
    {
        if(!ReferenceEquals(prepared.Owner,this)||closed||prepared.Note.IsClosed||ocrAuthorityEpoch!=prepared.AuthorityEpoch||!ReferenceEquals(basis,prepared.ExpectedBasis)||notes.Count!=prepared.Membership.Length)return false;
        for(int i=0;i<notes.Count;i++)if(!ReferenceEquals(notes[i],prepared.Membership[i].Note)||notes[i].EditVersion!=prepared.Membership[i].Version||!acceptedVersions.TryGetValue(notes[i].Id,out long accepted)||accepted!=notes[i].EditVersion)return false;
        return IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint);
    }
    internal PreparedBranchResolution PrepareBranchResolution(Guid noteId,Guid pendingTipRevisionId,Action<VaultSnapshot> validateCurrent)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(validateCurrent);
        if(mergePublishing||ocrPublishing||branchResolutionPublishing)throw new InvalidOperationException("Resolution publication occupied");
        if(noteId==Guid.Empty||pendingTipRevisionId==Guid.Empty)throw new ArgumentException("Resolution identity");
        var note=notes.SingleOrDefault(n=>n.Id==noteId)??throw new InvalidOperationException("Resolution note missing");RequireNote(note);
        if(notes.Any(n=>!acceptedVersions.TryGetValue(n.Id,out long version)||version!=n.EditVersion))throw new InvalidOperationException("Resolution requires a settled clean basis");
        var expected=basis;long authorityEpoch=ocrAuthorityEpoch;var membership=notes.Select(n=>(Note:n,Version:n.EditVersion)).ToArray();byte[] fingerprint=MergeFingerprint();
        var before=OwnMergeSnapshot(Capture());VaultEnvelope.Validate(before);var old=before.Notes.Single(n=>n.NoteId==noteId);
        if(old.Scope!="device-only"||old.Metadata.Deleted)throw new InvalidOperationException("Resolution current scope or trash");
        var history=before.History.Where(r=>r.NoteId==noteId).ToArray();var tip=history.SingleOrDefault(r=>r.RevisionId==pendingTipRevisionId);
        if(tip is null||tip.Metadata.Deleted||!RevisionBranchAnalysis.PendingBranchTips(old,history).Contains(pendingTipRevisionId))throw new InvalidOperationException("Resolution requires an explicit live maximal pending tip");
        var replacement=old with{RevisionId=Guid.NewGuid(),Parents=[old.RevisionId,pendingTipRevisionId],ModifiedAt=clock.GetUtcNow()};
        var candidate=before with{Notes=before.Notes.Select(n=>n.NoteId==noteId?replacement:n).ToArray(),History=before.History.Append(AsRevision(old)).ToArray()};
        VaultEnvelope.Validate(candidate);DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,candidate);
        var remaining=RevisionBranchAnalysis.PendingBranchTips(replacement,candidate.History.Where(r=>r.NoteId==noteId).ToArray()).ToImmutableArray();
        var prepared=new PreparedBranchResolution(this,expected,fingerprint,candidate,note,membership,authorityEpoch,old.RevisionId,pendingTipRevisionId,remaining);
        if(!IsResolutionCurrent(prepared))throw new InvalidOperationException("Resolution basis changed during preparation");
        validateCurrent(candidate);
        if(!IsResolutionCurrent(prepared))throw new InvalidOperationException("Resolution validation changed authority");
        return prepared;
    }
    internal bool ApplyPreparedBranchResolution(PreparedBranchResolution prepared,Func<bool> current,Action markApplied)
    {
        ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(current);ArgumentNullException.ThrowIfNull(markApplied);
        // Consume before invoking any external predicate, including a failed attempt on another owner.
        if(prepared.Consumed)throw new InvalidOperationException("Resolution candidate consumed");prepared.Consumed=true;
        bool Live(){if(!IsResolutionCurrent(prepared))return false;bool allowed=current();return allowed&&IsResolutionCurrent(prepared);}
        EnsureOpen();if(mergePublishing||ocrPublishing||branchResolutionPublishing||!Live())throw new InvalidOperationException("Resolution authority ended");
        var candidate=prepared.Candidate;VaultEnvelope.Validate(candidate);DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,candidate);
        var replacement=candidate.Notes.Single(n=>n.NoteId==prepared.NoteId);
        var witnesses=DiscardedEvidence.Clone(candidate.DiscardedRevisions);var markers=DiscardedEvidence.ContentlessMarkers(candidate);
        var versions=notes.ToDictionary(n=>n.Id,n=>n.Id==prepared.NoteId?checked(n.EditVersion+1):n.EditVersion);
        var invalidations=AttachmentReadInvalidating?.GetInvocationList()??[];var observers=Changed?.GetInvocationList()??[];
        if(!Live())throw new InvalidOperationException("Resolution candidate authority ended");
        // The existing merge guard also blocks nested OCR/import publication during this phase.
        branchResolutionPublishing=mergePublishing=true;
        try
        {
            foreach(Action<NoteDraft?> handler in invalidations){handler(prepared.Note);if(!Live())throw new InvalidOperationException("Resolution invalidation changed authority");}
            if(!Live())throw new InvalidOperationException("Resolution publication authority ended");
            // No callbacks, validation, serialization, cloning, enumeration or growth through Applied.
            prepared.Note.StagePreservedRevision(replacement);basis=candidate;discardedRevisions=witnesses;discardedMarkers=markers;acceptedVersions=versions;
            prepared.Applied=true;
            try{markApplied();}catch{return false;}
            bool clean=true;
            foreach(Action observer in observers){try{observer();}catch{clean=false;}if(closed){clean=false;break;}}
            if(!closed){try{prepared.Note.PublishPreservedRevision();}catch{clean=false;}}else clean=false;
            return clean;
        }
        finally{branchResolutionPublishing=mergePublishing=false;}
    }
}

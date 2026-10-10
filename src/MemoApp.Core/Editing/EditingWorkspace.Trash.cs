using System.Collections.Immutable;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    internal Guid[] EligibleTrash(DateTimeOffset cutoff)
    {
        EnsureOpen();return notes.Where(n=>n.IsDeleted&&n.ModifiedAt<cutoff).Select(n=>n.Id).Order().ToArray();
    }
    internal Guid[] PurgeTrash(Guid[] expected,DateTimeOffset cutoff)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(expected);
        if(expected.Length>100||expected.Any(id=>id==Guid.Empty)||expected.Distinct().Count()!=expected.Length||!expected.Order().SequenceEqual(EligibleTrash(cutoff)))throw new InvalidOperationException("Exact current trash eligibility changed");
        if(expected.Length==0)return [];
        if(attachmentRootId==Guid.Empty)throw new InvalidOperationException("Prepared anchored root required before trash removal");
        var ids=expected.ToHashSet();var before=Capture();var removed=notes.Where(n=>ids.Contains(n.Id)).ToArray();
        // Only a settled accepted basis supplies stable revision identities for a backed-up removal.
        if(notes.Any(n=>!acceptedVersions.TryGetValue(n.Id,out var version)||version!=n.EditVersion))throw new InvalidOperationException("Save must settle all drafts before trash removal");
        var evidence=DiscardedEvidence.Clone(before.DiscardedRevisions).Concat(before.History.Where(h=>ids.Contains(h.NoteId)).Select(h=>new StoredDiscardedRevision(h.NoteId,h.RevisionId,(Guid[])h.Parents.Clone())))
            .Concat(before.Notes.Where(n=>ids.Contains(n.NoteId)).Select(n=>new StoredDiscardedRevision(n.NoteId,n.RevisionId,(Guid[])n.Parents.Clone()))).ToArray();
        var profiles=before.UiDevices.Select(d=>d with{Windows=d.Windows.Where(w=>w.NoteId is not Guid id||!ids.Contains(id)).ToImmutableArray(),RecentNoteIds=d.RecentNoteIds.Where(id=>!ids.Contains(id)).ToImmutableArray()}).ToArray();
        var candidate=before with{SchemaVersion=Math.Max(8,before.SchemaVersion),Notes=before.Notes.Where(n=>!ids.Contains(n.NoteId)).ToArray(),History=before.History.Where(h=>!ids.Contains(h.NoteId)).ToArray(),DiscardedRevisions=evidence,UiDevices=profiles};
        VaultEnvelope.Validate(candidate);DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,candidate);
        // Complete authoritative state and every retained draft before any native observer runs.
        AcceptPrepared(candidate);devices.Clear();devices.AddRange(profiles);
        foreach(var draft in removed){notes.Unregister(draft);acceptedVersions.Remove(draft.Id);draft.StageClose();}
        List<Exception> failures=[];
        if(Changed is { } handlers)foreach(Action handler in handlers.GetInvocationList())try{handler();}catch(Exception error){failures.Add(error);}
        foreach(var draft in removed)try{draft.PublishClosed();}catch(Exception error){failures.Add(error);}
        if(!closed)try{notes.PublishReset();}catch(Exception error){failures.Add(error);}
        if(failures.Count!=0)throw new AggregateException("Trash removal applied; observer publication failed",failures);
        return expected.ToArray();
    }
}

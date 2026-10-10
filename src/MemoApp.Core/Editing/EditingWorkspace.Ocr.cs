using System.Collections.Immutable;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    private bool ocrPublishing;
    internal bool IsOcrPublicationActive=>ocrPublishing;
    internal ImmutableArray<StoredAttachmentOcrResult> DescribeAttachmentOcrResults(NoteDraft note)
    {RequireNote(note,true);return note.Metadata.AttachmentOcrResults.Where(r=>note.AttachmentIds.Contains(r.ObjectId)).ToImmutableArray();}
    private void PreflightOcrEdit(NoteDraft note,string title,string text,NoteMetadata metadata)
    {
        if(!ocrEnabled)return;
        RequireNote(note);var before=Capture();var old=before.Notes.Single(n=>n.NoteId==note.Id);
        var history=before.History;
        if(acceptedVersions.TryGetValue(note.Id,out long version)&&version==note.EditVersion)
            history=history.Append(new StoredRevision(old.NoteId,old.RevisionId,old.Parents,old.ModifiedAt,old.Title,old.Text){Metadata=old.Metadata,Mode=old.Mode,Document=old.Document,AttachmentIds=old.AttachmentIds}).ToArray();
        var candidate=before with{Notes=before.Notes.Select(n=>n.NoteId==note.Id?n with{Title=title,Text=text,Metadata=metadata}:n).ToArray(),History=history};
        // Hidden recovery may contain a repairable ordinary title/body; OCR authority still has strict budgets.
        AttachmentOcrValidation.Snapshot(candidate);
        SnapshotValidation.PayloadBudget(candidate);
    }
    internal void ApplyAttachmentOcrResult(NoteDraft note,StoredAttachmentOcrResult result,Func<bool>? current=null,Action? markApplied=null)
    {
        RequireNote(note);ArgumentNullException.ThrowIfNull(result);
        if(ocrPublishing||mergePublishing||note.Metadata.AttachmentOcrResults.Any(r=>r.ObjectId==result.ObjectId))throw new InvalidOperationException("OCR publication occupied or duplicate");
        long expected=note.EditVersion;var expectedBasis=basis;long expectedEpoch=ocrAuthorityEpoch;
        var originalNotes=notes.Select(d=>(Draft:d,Version:d.EditVersion)).ToArray();
        bool Pure()=>!closed&&!note.IsClosed&&note.EditVersion==expected&&ocrAuthorityEpoch==expectedEpoch&&ReferenceEquals(basis,expectedBasis)&&notes.Count==originalNotes.Length&&originalNotes.Select((entry,i)=>ReferenceEquals(notes[i],entry.Draft)&&notes[i].EditVersion==entry.Version).All(v=>v);
        bool Live(){if(!Pure())return false;bool authorized=current?.Invoke()??true;return authorized&&Pure();}
        if(!Live())throw new InvalidOperationException("OCR authority ended");
        var before=Capture();VaultEnvelope.Validate(before);var old=before.Notes.Single(n=>n.NoteId==note.Id);
        var replacement=old with{RevisionId=Guid.NewGuid(),Parents=[old.RevisionId],ModifiedAt=clock.GetUtcNow(),Metadata=old.Metadata with{AttachmentOcrResults=old.Metadata.AttachmentOcrResults.Add(result)}};
        var revision=new StoredRevision(old.NoteId,old.RevisionId,(Guid[])old.Parents.Clone(),old.ModifiedAt,old.Title,old.Text){Metadata=old.Metadata,Mode=old.Mode,Document=old.Document,AttachmentIds=old.AttachmentIds};
        var candidate=before with{SchemaVersion=11,Notes=before.Notes.Select(n=>n.NoteId==note.Id?replacement:n).ToArray(),History=before.History.Append(revision).ToArray()};
        VaultEnvelope.Validate(candidate);
        var witnesses=DiscardedEvidence.Clone(candidate.DiscardedRevisions);var markers=DiscardedEvidence.ContentlessMarkers(candidate);
        DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,candidate);
        var versions=notes.ToDictionary(d=>d.Id,d=>d.Id==note.Id?checked(d.EditVersion+1):d.EditVersion);
        var invalidations=AttachmentReadInvalidating?.GetInvocationList()??[];var observers=Changed?.GetInvocationList()??[];
        if(!Live())throw new InvalidOperationException("OCR candidate authority ended");
        ocrPublishing=true;
        try
        {
            foreach(Action<NoteDraft?> handler in invalidations){handler(note);if(!Live())throw new InvalidOperationException("OCR invalidation changed authority");}
            if(!Live())throw new InvalidOperationException("OCR publication authority ended");
            // All candidate validation, cloning and callback enumeration preceded this field swap.
            note.StageOcrMetadata(replacement);basis=candidate;ocrEnabled=inlineImagesEnabled=backupPolicyEnabled=discardedEnabled=filePathsEnabled=searchStateEnabled=true;
            discardedRevisions=witnesses;discardedMarkers=markers;
            acceptedVersions=versions;
            markApplied?.Invoke();
            foreach(Action handler in observers)handler();
            if(!closed)note.PublishEvent();
        }
        finally{ocrPublishing=false;}
    }
}

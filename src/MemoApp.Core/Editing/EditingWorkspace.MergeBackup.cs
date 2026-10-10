using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Globalization;
using MemoApp.Core.History;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

internal sealed class PreparedBackupMerge(VaultSnapshot expectedBasis,byte[] fingerprint,VaultSnapshot candidate,int imported,ImmutableArray<BackupMergePendingBranch> pending)
{
    internal readonly VaultSnapshot ExpectedBasis=expectedBasis,Candidate=candidate;
    internal readonly byte[] Fingerprint=fingerprint;
    internal readonly int Imported=imported;
    internal readonly ImmutableArray<BackupMergePendingBranch> Pending=pending;
    internal bool Consumed;
}
public sealed record BackupMergePendingBranch(Guid NoteId,Guid RevisionId);
public sealed record BackupMergeBranchContent(Guid RevisionId,string Title,string TextExcerpt,bool Truncated,string Mode,DateTimeOffset ModifiedAt,int AttachmentCount);
public sealed record BackupMergeBranchComparison(Guid NoteId,BackupMergeBranchContent Current,BackupMergeBranchContent Incoming);
public enum BackupMergeDisposition { NotApplied,NoChange,AppliedAndSaved,AppliedDirty }
public sealed record BackupMergeResult(BackupMergeDisposition Disposition,int ImportedRevisionCount,ImmutableArray<BackupMergePendingBranch> PendingBranches);

public sealed partial class EditingWorkspace
{
    private bool mergePublishing;
    internal bool IsMergePublicationActive=>mergePublishing;
    internal VaultSnapshot MergeBasis=>basis;
    internal bool IsMergeBasisCurrent(VaultSnapshot expected,byte[] fingerprint)
    {
        if(closed||!ReferenceEquals(basis,expected)||notes.Any(n=>!acceptedVersions.TryGetValue(n.Id,out var version)||n.EditVersion!=version))return false;
        return CryptographicOperations.FixedTimeEquals(fingerprint,MergeFingerprint());
    }
    internal byte[] MergeFingerprint()
    {
        var bytes=SnapshotSerialization.Bytes(Capture());try{return SHA256.HashData(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public Guid[] PendingBackupBranchTips(NoteDraft note)
    {
        RequireNote(note,true);var snapshot=Capture();VaultEnvelope.Validate(snapshot);
        return RevisionBranchAnalysis.PendingBranchTips(snapshot.Notes.Single(n=>n.NoteId==note.Id),snapshot.History.Where(r=>r.NoteId==note.Id).ToArray());
    }
    private static VaultSnapshot OwnMergeSnapshot(VaultSnapshot snapshot)
    {
        var bytes=SnapshotSerialization.Bytes(snapshot);
        try{return VaultEnvelope.ReadSnapshot(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public ImmutableArray<BackupMergeBranchComparison> DescribePendingBackupBranches(NoteDraft note)
    {
        RequireNote(note,true);if(notes.Any(n=>!acceptedVersions.TryGetValue(n.Id,out var version)||n.EditVersion!=version))throw new InvalidOperationException("Save current edits before branch comparison");
        var snapshot=Capture();VaultEnvelope.Validate(snapshot);var current=snapshot.Notes.Single(n=>n.NoteId==note.Id);
        return BranchDetails(snapshot,RevisionBranchAnalysis.PendingBranchTips(current,snapshot.History.Where(r=>r.NoteId==note.Id).ToArray()).Select(id=>new BackupMergePendingBranch(note.Id,id)).ToImmutableArray());
    }
    internal static ImmutableArray<BackupMergeBranchComparison> BranchDetails(VaultSnapshot snapshot,ImmutableArray<BackupMergePendingBranch> branches)
    {
        if(branches.Length>10000)throw new InvalidDataException("Branch detail bound");
        BackupMergeBranchContent Describe(StoredRevision r)
        {
            string text=r.Text;
            if(text.Length>256){int end=0;foreach(int boundary in StringInfo.ParseCombiningCharacters(text)){if(boundary>255)break;end=boundary;}text=text[..end]+"…";}
            return new(r.RevisionId,r.Title,text,r.Text.Length>256,r.Mode,r.ModifiedAt,r.AttachmentIds.Length);
        }
        var currents=snapshot.Notes.ToDictionary(n=>n.NoteId,n=>Describe(AsRevision(n)));var history=snapshot.History.ToDictionary(r=>r.RevisionId);
        return branches.Select(b=>new BackupMergeBranchComparison(b.NoteId,currents[b.NoteId],Describe(history[b.RevisionId]))).ToImmutableArray();
    }
    private static StoredRevision AsRevision(StoredNote n)=>new(n.NoteId,n.RevisionId,(Guid[])n.Parents.Clone(),n.ModifiedAt,n.Title,n.Text)
        {Mode=n.Mode,Document=n.Document,Metadata=n.Metadata,AttachmentIds=n.AttachmentIds};
    private static bool SameRevision(StoredRevision a,StoredRevision b)
    {
        var x=a.Metadata;var y=b.Metadata;
        return a.NoteId==b.NoteId&&a.RevisionId==b.RevisionId&&a.Parents.SequenceEqual(b.Parents)&&a.ModifiedAt==b.ModifiedAt&&a.Title==b.Title&&a.Text==b.Text&&a.Mode==b.Mode&&a.Document==b.Document&&a.AttachmentIds.SequenceEqual(b.AttachmentIds)
            &&x.FolderId==y.FolderId&&x.TagIds.SequenceEqual(y.TagIds)&&x.Color==y.Color&&x.Important==y.Important&&x.Favorite==y.Favorite&&x.Pinned==y.Pinned&&x.Archived==y.Archived&&x.Deleted==y.Deleted&&x.Order==y.Order&&x.FilePathLinks.SequenceEqual(y.FilePathLinks)&&x.AttachmentOcrResults.SequenceEqual(y.AttachmentOcrResults);
    }
    internal PreparedBackupMerge PrepareBackupMerge(VaultSnapshot backup,Guid[] selected,Action<VaultSnapshot> validateCurrent)
    {
        EnsureOpen();if(mergePublishing)throw new InvalidOperationException("Merge publication occupied");
        ArgumentNullException.ThrowIfNull(backup);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(validateCurrent);
        if(selected.Length is <1 or >100||selected.Any(id=>id==Guid.Empty)||selected.Distinct().Count()!=selected.Length)throw new ArgumentException("Merge selection bounds");
        var ids=(Guid[])selected.Clone();VaultEnvelope.Validate(backup);var owned=OwnMergeSnapshot(backup);
        if(notes.Any(n=>!acceptedVersions.TryGetValue(n.Id,out var version)||n.EditVersion!=version))throw new InvalidOperationException("Merge requires settled drafts");
        var expected=basis;var fingerprint=MergeFingerprint();var before=OwnMergeSnapshot(Capture());
        var all=before.History.Concat(before.Notes.Select(AsRevision)).ToDictionary(r=>r.RevisionId);
        var forbidden=before.DiscardedRevisions.Select(r=>r.RevisionId).Concat(before.Tombstones.Where(t=>before.Notes.All(n=>n.NoteId!=t.NoteId)).Select(t=>t.RevisionId)).ToHashSet();
        var incoming=new List<StoredRevision>();var history=before.History.ToList();int count=0;
        foreach(var id in ids)
        {
            var current=before.Notes.SingleOrDefault(n=>n.NoteId==id);var source=owned.Notes.SingleOrDefault(n=>n.NoteId==id);
            if(current is null||source is null||current.Metadata.Deleted||source.Metadata.Deleted||before.Tombstones.Any(t=>t.NoteId==id)||owned.Tombstones.Any(t=>t.NoteId==id)||before.DiscardedRevisions.Any(r=>r.NoteId==id)||owned.DiscardedRevisions.Any(r=>r.NoteId==id))throw new InvalidOperationException("Merge supports retained live same-ID notes only");
            if(current.CreatedAt!=source.CreatedAt||current.Scope!=source.Scope)throw new InvalidOperationException("Merge note identity conflict");
            incoming.AddRange(owned.History.Where(r=>r.NoteId==id));incoming.Add(AsRevision(source));
        }
        foreach(var revision in incoming)
        {
            if(forbidden.Contains(revision.RevisionId))throw new InvalidOperationException("Discarded revision identity conflict");
            if(all.TryGetValue(revision.RevisionId,out var old)){if(!SameRevision(old,revision))throw new InvalidOperationException("Immutable revision identity conflict");}
            else{all.Add(revision.RevisionId,revision);history.Add(revision);count++;}
        }
        var nextFolders=before.Folders.ToList();var sourceFolders=owned.Folders.ToDictionary(f=>f.FolderId);var included=new HashSet<Guid>();
        void IncludeFolder(Guid id)
        {
            if(!included.Add(id))return;var folder=sourceFolders[id];if(folder.ParentId is Guid parent)IncludeFolder(parent);
            var old=nextFolders.FirstOrDefault(f=>f.FolderId==id);if(old is null)nextFolders.Add(folder);else if(old!=folder)throw new InvalidOperationException("Merge folder identity conflict");
        }
        foreach(var r in incoming)if(r.Metadata.FolderId is Guid id)IncludeFolder(id);
        var nextTags=before.Tags.ToList();var sourceTags=owned.Tags.ToDictionary(t=>t.TagId);
        foreach(var id in incoming.SelectMany(r=>r.Metadata.TagIds).Distinct())
        {var tag=sourceTags[id];var old=nextTags.FirstOrDefault(t=>t.TagId==id);if(old is null)nextTags.Add(tag);else if(old!=tag)throw new InvalidOperationException("Merge tag identity conflict");}
        var objects=before.AttachmentObjects.ToList();var sourceObjects=owned.AttachmentObjects.ToDictionary(o=>o.ObjectId);var refs=incoming.SelectMany(r=>r.AttachmentIds).Distinct().ToArray();
        if(refs.Length>0&&(before.AttachmentRootId==Guid.Empty||before.AttachmentRootId!=owned.AttachmentRootId))throw new InvalidOperationException("Merge attachment root differs");
        foreach(var id in refs)
        {var obj=sourceObjects[id];var old=objects.FirstOrDefault(o=>o.ObjectId==id);if(old is null)objects.Add(obj);else if(!AttachmentValidation.SameObject(old,obj))throw new InvalidOperationException("Immutable merge object conflict");}
        int schema=owned.SchemaVersion==11?11:before.SchemaVersion;
        if(incoming.Any(r=>r.Metadata.FilePathLinks.Length>0))schema=Math.Max(schema,7);
        if(incoming.Any(r=>r.Document?.SchemaVersion==2))schema=Math.Max(schema,10);
        if(schema>=5&&before.AttachmentRootId==Guid.Empty)throw new InvalidOperationException("Merge cannot create attachment root");
        var candidate=before with{SchemaVersion=schema,History=history.ToArray(),Folders=nextFolders.ToArray(),Tags=nextTags.ToArray(),AttachmentObjects=objects.ToImmutableArray()};
        VaultEnvelope.Validate(candidate);validateCurrent(candidate);
        if(!IsMergeBasisCurrent(expected,fingerprint))throw new InvalidOperationException("Merge basis changed during validation");
        var pending=ids.SelectMany(id=>RevisionBranchAnalysis.PendingBranchTips(candidate.Notes.Single(n=>n.NoteId==id),candidate.History.Where(r=>r.NoteId==id).ToArray()).Select(tip=>new BackupMergePendingBranch(id,tip))).ToImmutableArray();
        return new(expected,fingerprint,candidate,count,pending);
    }
    // All fallible allocations and handler enumeration precede the callback-free field installation.
    internal bool ApplyPreparedBackupMerge(PreparedBackupMerge prepared,Func<bool> current,Action markApplied)
    {
        EnsureOpen();if(mergePublishing||prepared.Consumed||!IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint)||!current())throw new InvalidOperationException("Merge publication authority ended");
        prepared.Consumed=true;if(prepared.Imported==0)return true;
        var candidate=prepared.Candidate;VaultEnvelope.Validate(candidate);
        var witnesses=DiscardedEvidence.Clone(candidate.DiscardedRevisions);var markers=DiscardedEvidence.ContentlessMarkers(candidate);
        DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,candidate);
        folders.EnsureCapacity(candidate.Folders.Length);tags.EnsureCapacity(candidate.Tags.Length);
        var invalidations=AttachmentReadInvalidating?.GetInvocationList()??[];var observers=Changed?.GetInvocationList()??[];
        mergePublishing=true;
        try
        {
            foreach(Action<NoteDraft?> handler in invalidations)
            {
                handler(null);
                if(closed||!IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint)||!current())throw new InvalidOperationException("Merge invalidation changed authority");
            }
            if(!current()||!IsMergeBasisCurrent(prepared.ExpectedBasis,prepared.Fingerprint))throw new InvalidOperationException("Merge publication changed authority");
            // No callbacks, validation, cloning or capacity growth from here through markApplied.
            folders.Clear();folders.AddRange(candidate.Folders);tags.Clear();tags.AddRange(candidate.Tags);
            discardedRevisions=witnesses;discardedMarkers=markers;attachmentRootId=candidate.AttachmentRootId;attachmentObjects=candidate.AttachmentObjects;basis=candidate;
            ocrEnabled|=candidate.SchemaVersion==11;inlineImagesEnabled|=candidate.SchemaVersion>=10;backupPolicyEnabled|=candidate.SchemaVersion>=9;discardedEnabled|=candidate.SchemaVersion>=8;filePathsEnabled|=candidate.SchemaVersion>=7;searchStateEnabled|=candidate.SchemaVersion>=6;
            markApplied();bool cleanNotifications=true;
            foreach(Action handler in observers){try{handler();}catch{cleanNotifications=false;}if(closed){cleanNotifications=false;break;}}
            return cleanNotifications;
        }
        finally{mergePublishing=false;}
    }
}

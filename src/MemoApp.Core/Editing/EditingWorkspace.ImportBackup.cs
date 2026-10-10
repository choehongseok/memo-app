using System.Collections.Immutable;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    internal NoteDraft[] ImportBackupNotes(VaultSnapshot backup,Guid[] selected,Action<VaultSnapshot> validateCurrent)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(backup);ArgumentNullException.ThrowIfNull(selected);ArgumentNullException.ThrowIfNull(validateCurrent);VaultEnvelope.Validate(backup);
        if(selected.Length is <1 or >100||selected.Any(id=>id==Guid.Empty)||selected.Distinct().Count()!=selected.Length)throw new ArgumentException("Selected backup count/IDs");
        var lookup=backup.Notes.ToDictionary(n=>n.NoteId);var incoming=selected.Select(id=>lookup.TryGetValue(id,out var note)?note:throw new ArgumentException("Selected source note missing")).ToArray();var before=Capture();
        var folders=before.Folders.ToList();var sourceFolders=backup.Folders.ToDictionary(f=>f.FolderId);var visited=new HashSet<Guid>();
        void IncludeFolder(Guid id)
        {
            if(!visited.Add(id))return;var source=sourceFolders[id];if(source.ParentId is Guid parent)IncludeFolder(parent);var existing=folders.FirstOrDefault(f=>f.FolderId==id);if(existing is null)folders.Add(source);else if(existing!=source)throw new InvalidOperationException("Backup folder identity conflict");
        }
        foreach(var note in incoming)if(note.Metadata.FolderId is Guid id)IncludeFolder(id);
        var tags=before.Tags.ToList();var sourceTags=backup.Tags.ToDictionary(t=>t.TagId);foreach(var id in incoming.SelectMany(n=>n.Metadata.TagIds).Distinct()){var source=sourceTags[id];var existing=tags.FirstOrDefault(t=>t.TagId==id);if(existing is null)tags.Add(source);else if(existing!=source)throw new InvalidOperationException("Backup tag identity conflict");}
        var objects=before.AttachmentObjects.ToList();var sourceObjects=backup.AttachmentObjects.ToDictionary(o=>o.ObjectId);var objectIds=incoming.SelectMany(n=>n.AttachmentIds).Distinct().ToArray();if(objectIds.Length>0&&before.AttachmentRootId!=backup.AttachmentRootId)throw new InvalidOperationException("Backup attachment root differs");
        foreach(var id in objectIds){var source=sourceObjects[id];var existing=objects.FirstOrDefault(o=>o.ObjectId==id);if(existing is null)objects.Add(source);else if(!AttachmentValidation.SameObject(existing,source))throw new InvalidOperationException("Backup immutable object identity conflict");}
        int order=NextOrder();var now=clock.GetUtcNow();var sources=incoming.Select((n,i)=>n with{NoteId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Parents=[],ModifiedAt=now,Metadata=n.Metadata with{Deleted=false,Order=Math.Min(1000000,order+i)}}).ToArray();
        var candidate=before with{Notes=before.Notes.Concat(sources).ToArray(),Folders=folders.ToArray(),Tags=tags.ToArray(),AttachmentObjects=objects.ToImmutableArray()};if(backup.SchemaVersion==11)candidate=candidate with{SchemaVersion=11};if(candidate.SchemaVersion<10&&incoming.Any(n=>n.Document?.SchemaVersion==2))candidate=candidate with{SchemaVersion=10};if(candidate.SchemaVersion<7&&incoming.Any(n=>n.Metadata.FilePathLinks.Length>0))candidate=candidate with{SchemaVersion=7};VaultEnvelope.Validate(candidate);validateCurrent(candidate);EnsureOpen();
        var added=sources.Select(n=>new NoteDraft(clock,n)).ToArray();foreach(var draft in added)AddDraft(draft);this.folders.Clear();this.folders.AddRange(candidate.Folders);this.tags.Clear();this.tags.AddRange(candidate.Tags);AcceptPrepared(candidate);Changed?.Invoke();foreach(var draft in added){if(closed)break;notes.PublishAdded(draft);}return added;
    }
}

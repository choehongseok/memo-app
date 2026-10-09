using System.Collections.Immutable;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    private void RequireSearchRoot(){EnsureOpen();if(attachmentRootId==Guid.Empty)throw new InvalidOperationException("Search UI metadata requires prepared root");}
    public void RecordRecentNote(Guid profile,NoteDraft note)
    {
        RequireSearchRoot();RequireNote(note);var device=GetUiDevice(profile);
        var next=new[]{note.Id}.Concat(device.RecentNoteIds.Where(id=>id!=note.Id)).Take(20).ToImmutableArray();
        if(device.RecentNoteIds.SequenceEqual(next))return;SetUiDevice(device with{RecentNoteIds=next},true);
    }
    public NoteDraft[] RecentNotes(Guid profile)
    {
        var device=GetUiDevice(profile);return device.RecentNoteIds.Select(id=>notes.SingleOrDefault(n=>n.Id==id&&!n.IsClosed&&!n.IsDeleted)).Where(n=>n is not null).Cast<NoteDraft>().ToArray();
    }
    public void ClearRecentNotes(Guid profile)
    {
        var device=GetUiDevice(profile);if(device.RecentNoteIds.Length==0)return;RequireSearchRoot();SetUiDevice(device with{RecentNoteIds=[]},true);
    }
    public StoredSavedSearch SaveSearch(Guid profile,string name,SearchOptions options)
    {
        RequireSearchRoot();var device=GetUiDevice(profile);var previous=device.SavedSearches.FirstOrDefault(s=>string.Equals(s.Name,name,StringComparison.OrdinalIgnoreCase));
        var saved=new StoredSavedSearch(previous?.Id??Guid.NewGuid(),name,options);if(previous==saved)return previous;
        SetUiDevice(device with{SavedSearches=device.SavedSearches.Where(s=>s.Id!=saved.Id).Append(saved).ToImmutableArray()},true);return saved;
    }
    public void RemoveSavedSearch(Guid profile,Guid id)
    {
        var device=GetUiDevice(profile);if(device.SavedSearches.All(s=>s.Id!=id))return;RequireSearchRoot();SetUiDevice(device with{SavedSearches=device.SavedSearches.Where(s=>s.Id!=id).ToImmutableArray()},true);
    }
    private StoredDeviceUi[] SanitizeRecent(StoredNote[] nextNotes)
    {
        var active=nextNotes.Where(n=>!n.Metadata.Deleted).Select(n=>n.NoteId).ToHashSet();return devices.Select(d=>d with{RecentNoteIds=d.RecentNoteIds.Where(active.Contains).ToImmutableArray()}).ToArray();
    }
}

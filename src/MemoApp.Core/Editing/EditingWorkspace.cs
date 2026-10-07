using System.Collections.Immutable;
using System.Collections.ObjectModel;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

public sealed class EditingWorkspace
{
    private readonly TimeProvider clock;
    private readonly NoteCollection notes = [];
    private readonly List<StoredFolder> folders = [];
    private readonly List<StoredTag> tags = [];
    private readonly List<StoredDeviceUi> devices = [];
    private readonly Dictionary<Guid, long> acceptedVersions = [];
    private VaultSnapshot basis;
    private bool closed;
    public EditingWorkspace(TimeProvider clock, VaultSnapshot? initial = null, VaultSnapshot? displayed = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); Notes = new(notes);
        // Keep the last accepted revision basis separate from unsaved (possibly invalid) drafts.
        // Recovery supplies its entire latest organization state, not a stale vault.Loaded projection.
        basis = initial ?? new(3, Guid.NewGuid(), []);
        var visible = displayed ?? basis;
        folders.AddRange(visible.Folders); tags.AddRange(visible.Tags); devices.AddRange(visible.UiDevices);
        foreach (var source in visible.Notes)
        {
            AddDraft(new(clock, source));
            var old = basis.Notes.FirstOrDefault(n => n.NoteId == source.NoteId);
            acceptedVersions[source.NoteId] = old is not null && old.Title == source.Title && old.Text == source.Text && old.Metadata == source.Metadata ? 0 : -1;
        }
    }
    public ReadOnlyObservableCollection<NoteDraft> Notes { get; }
    public IReadOnlyList<StoredFolder> Folders => folders.AsReadOnly();
    public IReadOnlyList<StoredTag> Tags => tags.AsReadOnly();
    public event Action? Changed;
    public StoredDeviceUi GetUiDevice(Guid profile)
    {
        EnsureOpen(); if(profile==Guid.Empty)throw new ArgumentException("Empty UI profile");
        return devices.SingleOrDefault(d=>d.UiDeviceId==profile) ?? new(profile,new(),[]);
    }
    private void SetUiDevice(StoredDeviceUi next)
    {
        EnsureOpen(); var updated=devices.Where(d=>d.UiDeviceId!=next.UiDeviceId).Append(next).ToArray();
        var candidate=Capture() with {UiDevices=updated}; VaultEnvelope.Validate(candidate);
        if(devices.SingleOrDefault(d=>d.UiDeviceId==next.UiDeviceId)==next)return;
        devices.Clear();devices.AddRange(updated);Changed?.Invoke();
    }
    public void SetUiPreferences(Guid profile,UiPreferences preferences)
    {
        var device=GetUiDevice(profile); if(device.Preferences==preferences)return;
        SetUiDevice(device with {Preferences=preferences});
    }
    public void SetWindowLayout(Guid profile,StoredWindowLayout layout)
    {
        var device=GetUiDevice(profile);
        if(device.Windows.Any(w=>w.Kind==layout.Kind&&w.NoteId==layout.NoteId&&w==layout))return;
        SetUiDevice(device with {Windows=device.Windows.Where(w=>w.Kind!=layout.Kind||w.NoteId!=layout.NoteId).Append(layout).ToImmutableArray()});
    }
    internal VaultSnapshot FrozenBasis => basis;
    private void EnsureOpen() { if (closed) throw new InvalidOperationException("Editing workspace is closed"); }
    private void RequireNote(NoteDraft note, bool allowTrash = false)
    {
        EnsureOpen();
        if (!notes.Contains(note) || note.IsClosed || note.IsDeleted && !allowTrash) throw new InvalidOperationException("Note is not editable in this workspace");
    }
    private void AddDraft(NoteDraft note)
    {
        note.PropertyChanged += (_, e) => { if (!closed && e.PropertyName == nameof(NoteDraft.EditVersion)) Changed?.Invoke(); };
        notes.Register(note);
    }
    public NoteDraft CreateNote()
    {
        EnsureOpen();
        if (notes.Count >= 100) throw new InvalidOperationException("Trial note limit reached (including trash)");
        var note = new NoteDraft(clock, NextOrder()); AddDraft(note);
        Changed?.Invoke(); if (!closed) notes.PublishAdded(note); return note;
    }
    private static string Name(string value)
    {
        ArgumentNullException.ThrowIfNull(value); value = value.Trim();
        if (value.Length is < 1 or > 128 || value.Any(char.IsControl)) throw new ArgumentException("Name outside limits");
        return value;
    }
    public StoredFolder CreateFolder(string name, Guid? parentId = null)
    {
        EnsureOpen(); name = Name(name);
        if (folders.Count >= 100 || parentId is not null && folders.All(f => f.FolderId != parentId) || folders.Any(f => f.ParentId == parentId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Folder parent/name/limit rejected");
        var folder = new StoredFolder(Guid.NewGuid(), parentId, name); folders.Add(folder); Changed?.Invoke(); return folder;
    }
    public void MoveNote(NoteDraft note, Guid? folderId)
    {
        RequireNote(note);
        if (folderId is not null && folders.All(f => f.FolderId != folderId)) throw new ArgumentException("Unknown folder");
        note.SetMetadata(note.Metadata with { FolderId = folderId });
    }
    public void SetTags(NoteDraft note, IEnumerable<string> names)
    {
        RequireNote(note); ArgumentNullException.ThrowIfNull(names);
        var requested = names.Select(Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length > 16 || tags.Count + requested.Count(n => tags.All(t => !string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))) > 100) throw new InvalidOperationException("Tag budget exceeded");
        var ids = requested.Select(name =>
        {
            var existing = tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) { existing = new(Guid.NewGuid(), name); tags.Add(existing); }
            return existing.TagId;
        }).Order().ToImmutableArray();
        if (note.Metadata.TagIds.SequenceEqual(ids)) return;
        note.SetMetadata(note.Metadata with { TagIds = ids });
    }
    public void SetImportant(NoteDraft note, bool value) { RequireNote(note); note.Important = value; }
    private int NextOrder() => notes.Count == 0 ? 0 : Math.Min(1000000, notes.Max(n => n.Metadata.Order) + 1);
    private void ApplyEvent(NoteDraft note, string title, string text, NoteMetadata metadata) =>
        ApplyEvents(new Dictionary<Guid, (string Title, string Text, NoteMetadata Metadata)> { [note.Id] = (title, text, metadata) });
    private void ApplyEvents(IReadOnlyDictionary<Guid, (string Title, string Text, NoteMetadata Metadata)> changes)
    {
        if (changes.Count == 0) return;
        var before = Capture(); VaultEnvelope.Validate(before); var history = before.History.ToList();
        var nextNotes = before.Notes.Select(previous =>
        {
            if (!changes.TryGetValue(previous.NoteId, out var change)) return previous;
            history.Add(new(previous.NoteId, previous.RevisionId, (Guid[])previous.Parents.Clone(), previous.ModifiedAt, previous.Title, previous.Text) { Metadata = previous.Metadata });
            return previous with { RevisionId = Guid.NewGuid(), Parents = [previous.RevisionId], ModifiedAt = clock.GetUtcNow(), Title = change.Title, Text = change.Text, Metadata = change.Metadata };
        }).ToArray();
        var tombstones = before.Tombstones.Where(t => nextNotes.All(n => n.NoteId != t.NoteId))
            .Concat(nextNotes.Where(n => n.Metadata.Deleted).Select(n => new StoredTombstone(n.NoteId, n.RevisionId, (Guid[])n.Parents.Clone()))).ToArray();
        var next = before with { Notes = nextNotes, History = history.ToArray(), Tombstones = tombstones };
        VaultEnvelope.Validate(next);
        var affected = notes.Where(n => changes.ContainsKey(n.Id)).ToArray();
        foreach (var note in affected) note.StageEvent(nextNotes.Single(n => n.NoteId == note.Id));
        AcceptPrepared(next); Changed?.Invoke();
        foreach (var note in affected) { if (closed) break; note.PublishEvent(); }
    }
    public NoteDraft ImportText(string title, string text, Guid? folderId = null) =>
        AddComplete(title,text,new() { FolderId=folderId,Order=NextOrder() });
    private NoteDraft AddComplete(string title,string text,NoteMetadata metadata)
    {
        EnsureOpen(); var before = Capture(); var now = clock.GetUtcNow();
        var source = new StoredNote(Guid.NewGuid(), Guid.NewGuid(), [], now, now, title, text) { Metadata=metadata };
        var next = before with { Notes=before.Notes.Append(source).ToArray() }; VaultEnvelope.Validate(next);
        var draft = new NoteDraft(clock,source); AddDraft(draft); AcceptPrepared(next);
        Changed?.Invoke(); if (!closed) notes.PublishAdded(draft); return draft;
    }
    public void DeleteNote(NoteDraft note)
    {
        RequireNote(note); ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = true });
    }
    private NoteDraft[] BatchSelection(IEnumerable<NoteDraft> selected,bool deleted)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(selected);var batch=selected.Take(101).ToArray();
        if(batch.Length is <1 or >100 || batch.Any(n=>n is null) || batch.Select(n=>n.Id).Distinct().Count()!=batch.Length)throw new ArgumentException("Invalid batch selection/count");
        foreach(var note in batch){RequireNote(note,true);if(note.IsDeleted!=deleted)throw new InvalidOperationException("Batch contains incompatible note states");}
        return batch;
    }
    public void MoveNotes(IEnumerable<NoteDraft> selected,Guid? folderId)
    {
        var batch=BatchSelection(selected,false);
        if(folderId is Guid id && folders.All(f=>f.FolderId!=id))throw new ArgumentException("Unknown batch target folder");
        ApplyEvents(batch.Where(n=>n.FolderId!=folderId).ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{FolderId=folderId})));
    }
    public void DeleteNotes(IEnumerable<NoteDraft> selected)
    {
        var batch=BatchSelection(selected,false);ApplyEvents(batch.ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{Deleted=true})));
    }
    public void RestoreNotes(IEnumerable<NoteDraft> selected)
    {
        var batch=BatchSelection(selected,true);ApplyEvents(batch.ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{Deleted=false})));
    }
    public void RestoreNote(NoteDraft note)
    {
        RequireNote(note, true);
        if (!note.IsDeleted) return;
        ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = false });
    }
    public void ReorderBefore(NoteDraft note, NoteDraft target)
    {
        RequireNote(note); RequireNote(target);
        if (note == target) return;
        if (note.Pinned != target.Pinned) throw new InvalidOperationException("List-pinned and ordinary groups are separate");
        var ordered = notes.Where(n => !n.IsDeleted).OrderBy(n => n.Metadata.Order).ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToList();
        var original = ordered.ToArray(); ordered.Remove(note); ordered.Insert(ordered.IndexOf(target), note);
        if (ordered.SequenceEqual(original)) return;
        var changes = new Dictionary<Guid, (string Title, string Text, NoteMetadata Metadata)>();
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i].Metadata.Order != i) changes.Add(ordered[i].Id, (ordered[i].Title, ordered[i].Text, ordered[i].Metadata with { Order = i }));
        ApplyEvents(changes);
    }
    public NoteDraft Duplicate(NoteDraft note)
    {
        RequireNote(note); return AddComplete(note.Title,note.Text,note.Metadata with { Deleted=false,Order=NextOrder() });
    }
    public IReadOnlyList<StoredRevision> HistoryFor(NoteDraft note)
    {
        RequireNote(note, true);
        return basis.History.Where(h => h.NoteId == note.Id).Reverse().ToArray();
    }
    public void RestoreRevision(NoteDraft note, Guid revisionId)
    {
        RequireNote(note);
        var revision = basis.History.SingleOrDefault(h => h.NoteId == note.Id && h.RevisionId == revisionId) ?? throw new ArgumentException("Unknown revision");
        ApplyEvent(note, revision.Title, revision.Text, revision.Metadata with { Deleted = false });
    }
    public Guid[] DescendantFolders(Guid parentId)
    {
        EnsureOpen(); if (folders.All(f => f.FolderId != parentId)) throw new ArgumentException("Unknown folder");
        var ids = new HashSet<Guid> { parentId };
        bool added; do { added = false; foreach (var f in folders) if (f.ParentId is Guid p && ids.Contains(p)) added |= ids.Add(f.FolderId); } while (added);
        return ids.ToArray();
    }
    public VaultSnapshot Capture()
    {
        EnsureOpen(); var history = basis.History.ToList();
        var current = notes.Select(draft =>
        {
            var old = basis.Notes.FirstOrDefault(n => n.NoteId == draft.Id);
            if (old is not null && acceptedVersions.TryGetValue(draft.Id, out var accepted) && draft.EditVersion == accepted) return old;
            if (old is not null) history.Add(new(old.NoteId, old.RevisionId, (Guid[])old.Parents.Clone(), old.ModifiedAt, old.Title, old.Text) { Metadata = old.Metadata });
            return new StoredNote(draft.Id, Guid.NewGuid(), old is null ? [] : [old.RevisionId], draft.CreatedAt, draft.ModifiedAt, draft.Title, draft.Text) { Metadata = draft.Metadata };
        }).ToArray();
        var contentless = basis.Tombstones.Where(t => current.All(n => n.NoteId != t.NoteId));
        var tombstones = contentless.Concat(current.Where(n => n.Metadata.Deleted).Select(n => new StoredTombstone(n.NoteId, n.RevisionId, (Guid[])n.Parents.Clone()))).ToArray();
        return new(3, basis.DeviceId, current) { History = history.ToArray(), Tombstones = tombstones, Folders = folders.ToArray(), Tags = tags.ToArray(),UiDevices=devices.ToArray() };
    }
    public void AcceptPrepared(VaultSnapshot snapshot)
    {
        EnsureOpen(); basis = snapshot;
        foreach (var draft in notes) acceptedVersions[draft.Id] = draft.EditVersion;
    }
    public void Clear()
    {
        if (closed) return;
        closed = true; foreach (var note in notes.ToArray()) note.Close();
        notes.Clear(); folders.Clear(); tags.Clear(); devices.Clear();acceptedVersions.Clear(); basis = new(3, Guid.Empty, []);
    }
}

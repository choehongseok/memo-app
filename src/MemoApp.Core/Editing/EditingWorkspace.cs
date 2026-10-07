using System.Collections.Immutable;
using System.Collections.ObjectModel;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

public sealed class EditingWorkspace
{
    private readonly TimeProvider clock;
    private readonly ObservableCollection<NoteDraft> notes = [];
    private readonly List<StoredFolder> folders = [];
    private readonly List<StoredTag> tags = [];
    private readonly Dictionary<Guid, long> acceptedVersions = [];
    private VaultSnapshot basis;
    private bool closed;
    public EditingWorkspace(TimeProvider clock, VaultSnapshot? initial = null, VaultSnapshot? displayed = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); Notes = new(notes);
        // Keep the last accepted revision basis separate from unsaved (possibly invalid) drafts.
        // Recovery supplies its entire latest organization state, not a stale vault.Loaded projection.
        basis = initial ?? new(2, Guid.NewGuid(), []);
        var visible = displayed ?? basis;
        folders.AddRange(visible.Folders); tags.AddRange(visible.Tags);
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
    internal VaultSnapshot FrozenBasis => basis;
    private void EnsureOpen() { if (closed) throw new InvalidOperationException("Editing workspace is closed"); }
    private void RequireNote(NoteDraft note, bool allowTrash = false)
    {
        EnsureOpen();
        if (!notes.Contains(note) || note.IsClosed || note.IsDeleted && !allowTrash) throw new InvalidOperationException("Note is not editable in this workspace");
    }
    private void AddDraft(NoteDraft note)
    {
        notes.Add(note);
        note.PropertyChanged += (_, e) => { if (!closed && e.PropertyName == nameof(NoteDraft.EditVersion)) Changed?.Invoke(); };
    }
    public NoteDraft CreateNote()
    {
        EnsureOpen();
        if (notes.Count >= 100) throw new InvalidOperationException("Trial note limit reached (including trash)");
        var note = new NoteDraft(clock); AddDraft(note); Changed?.Invoke(); return note;
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
    private void ApplyEvent(NoteDraft note, string title, string text, NoteMetadata metadata)
    {
        // Preflight the entire next immutable state before changing any draft or accepted version.
        var before = Capture(); VaultEnvelope.Validate(before);
        var previous = before.Notes.Single(n => n.NoteId == note.Id);
        var nextNote = previous with { RevisionId = Guid.NewGuid(), Parents = [previous.RevisionId], ModifiedAt = clock.GetUtcNow(), Title = title, Text = text, Metadata = metadata };
        var nextNotes = before.Notes.Select(n => n.NoteId == note.Id ? nextNote : n).ToArray();
        var history = before.History.Append(new StoredRevision(previous.NoteId, previous.RevisionId, (Guid[])previous.Parents.Clone(), previous.ModifiedAt, previous.Title, previous.Text) { Metadata = previous.Metadata }).ToArray();
        var tombstones = before.Tombstones.Where(t => nextNotes.All(n => n.NoteId != t.NoteId))
            .Concat(nextNotes.Where(n => n.Metadata.Deleted).Select(n => new StoredTombstone(n.NoteId, n.RevisionId, (Guid[])n.Parents.Clone()))).ToArray();
        var next = before with { Notes = nextNotes, History = history, Tombstones = tombstones };
        VaultEnvelope.Validate(next);
        note.StageEvent(nextNote); AcceptPrepared(next); note.PublishEvent();
    }
    public void DeleteNote(NoteDraft note)
    {
        RequireNote(note); ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = true });
    }
    public void RestoreNote(NoteDraft note)
    {
        RequireNote(note, true);
        if (!note.IsDeleted) return;
        ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = false });
    }
    public NoteDraft Duplicate(NoteDraft note)
    {
        RequireNote(note); var copy = CreateNote(); copy.Title = note.Title; copy.Text = note.Text;
        copy.SetMetadata(note.Metadata with { Deleted = false }); return copy;
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
        return new(2, basis.DeviceId, current) { History = history.ToArray(), Tombstones = tombstones, Folders = folders.ToArray(), Tags = tags.ToArray() };
    }
    public void AcceptPrepared(VaultSnapshot snapshot)
    {
        basis = snapshot;
        foreach (var draft in notes) acceptedVersions[draft.Id] = draft.EditVersion;
    }
    public void Clear()
    {
        closed = true; foreach (var note in notes) note.Close();
        notes.Clear(); folders.Clear(); tags.Clear(); acceptedVersions.Clear(); basis = new(2, Guid.Empty, []);
    }
}

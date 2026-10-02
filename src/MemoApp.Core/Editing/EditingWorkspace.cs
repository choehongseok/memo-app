using System.Collections.ObjectModel;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// UI-thread session collection. It is not an encrypted repository or a lock implementation.
public sealed class EditingWorkspace
{
    private readonly TimeProvider clock;
    private readonly ObservableCollection<NoteDraft> notes = [];
    private VaultSnapshot basis;
    public EditingWorkspace(TimeProvider clock, VaultSnapshot? initial = null, VaultSnapshot? displayed = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Notes = new(notes);
        basis = initial ?? new(1, Guid.NewGuid(), []);
        foreach (var source in (displayed ?? basis).Notes) notes.Add(new(clock, source));
    }
    public ReadOnlyObservableCollection<NoteDraft> Notes { get; }
    public NoteDraft CreateNote()
    {
        if (notes.Count >= 100) throw new InvalidOperationException("Trial note limit reached");
        var note = new NoteDraft(clock);
        notes.Add(note);
        return note;
    }
    public VaultSnapshot Capture()
    {
        var history = basis.History.ToList();
        var current = notes.Select(draft =>
        {
            var old = basis.Notes.FirstOrDefault(n => n.NoteId == draft.Id);
            if (old is not null && old.Title == draft.Title && old.Text == draft.Text) return old;
            if (old is not null) history.Add(new(old.NoteId, old.RevisionId, old.Parents, old.ModifiedAt, old.Title, old.Text));
            return new StoredNote(draft.Id, Guid.NewGuid(), old is null ? [] : [old.RevisionId], draft.CreatedAt, draft.ModifiedAt, draft.Title, draft.Text);
        }).ToArray();
        return new(1, basis.DeviceId, current) { History = history.ToArray(), Tombstones = basis.Tombstones };
    }
    public void AcceptPrepared(VaultSnapshot snapshot) => basis = snapshot;
    public void Clear()
    {
        foreach (var note in notes) note.Close();
        notes.Clear();
        basis = new(1, Guid.NewGuid(), []);
    }
}

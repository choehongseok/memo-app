using System.Collections.ObjectModel;
namespace MemoApp.Core.Editing;

// UI-thread session collection. It is not an encrypted repository or a lock implementation.
public sealed class EditingWorkspace
{
    private readonly TimeProvider clock;
    private readonly ObservableCollection<NoteDraft> notes = [];
    public EditingWorkspace(TimeProvider clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Notes = new(notes);
    }
    public ReadOnlyObservableCollection<NoteDraft> Notes { get; }
    public NoteDraft CreateNote()
    {
        var note = new NoteDraft(clock);
        notes.Add(note);
        return note;
    }
    public void Clear()
    {
        foreach (var note in notes) note.Close();
        notes.Clear();
    }
}

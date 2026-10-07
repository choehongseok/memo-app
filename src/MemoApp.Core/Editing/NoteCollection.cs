using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace MemoApp.Core.Editing;
internal sealed class NoteCollection : ObservableCollection<NoteDraft>
{
    internal void Register(NoteDraft note) => Items.Add(note);
    internal void PublishAdded(NoteDraft note)
    {
        if (!Contains(note)) return;
        OnPropertyChanged(new(nameof(Count)));
        if (!Contains(note)) return;
        OnPropertyChanged(new("Item[]"));
        if (!Contains(note)) return;
        OnCollectionChanged(new(NotifyCollectionChangedAction.Add,note,IndexOf(note)));
    }
}

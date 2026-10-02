using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace MemoApp.Core.Editing;

// An unlocked in-memory draft; this type performs no I/O, key handling or autosave.
public sealed class NoteDraft : INotifyPropertyChanged
{
    private readonly TimeProvider clock;
    private string title = "";
    private string text = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    internal NoteDraft(TimeProvider clock)
    {
        this.clock = clock;
        Id = Guid.NewGuid();
        CreatedAt = ModifiedAt = clock.GetUtcNow();
    }
    public Guid Id { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ModifiedAt { get; private set; }
    public long EditVersion { get; private set; }
    public bool IsClosed { get; private set; }
    public string Title { get => title; set => Edit(ref title, value); }
    public string Text { get => text; set => Edit(ref text, value); }
    private void Edit(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (IsClosed) throw new InvalidOperationException("Editing session is closed");
        ArgumentNullException.ThrowIfNull(value);
        if (field == value) return;
        field = value;
        EditVersion++;
        ModifiedAt = clock.GetUtcNow();
        Notify(property);
        Notify(nameof(EditVersion));
        Notify(nameof(ModifiedAt));
    }
    internal void Close()
    {
        IsClosed = true;
        title = text = "";
        Notify(nameof(IsClosed));
        Notify(nameof(Title));
        Notify(nameof(Text));
    }
    private void Notify(string? property) => PropertyChanged?.Invoke(this, new(property));
}

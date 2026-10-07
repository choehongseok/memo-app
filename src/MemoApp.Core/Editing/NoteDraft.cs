using System.ComponentModel;
using System.Runtime.CompilerServices;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// Unlocked shared draft. No disk I/O, keys, network or persistent search index.
public sealed class NoteDraft : INotifyPropertyChanged
{
    private readonly TimeProvider clock;
    private string title = "", text = "";
    private NoteMetadata metadata = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    internal NoteDraft(TimeProvider clock, int order = 0)
    {
        this.clock = clock; metadata = new() { Order = order }; Id = Guid.NewGuid(); CreatedAt = ModifiedAt = clock.GetUtcNow();
    }
    internal NoteDraft(TimeProvider clock, StoredNote source) : this(clock)
    {
        Id = source.NoteId; CreatedAt = source.CreatedAt; ModifiedAt = source.ModifiedAt;
        title = source.Title; text = source.Text; metadata = source.Metadata;
    }
    public Guid Id { get; }
    public Guid? FolderId => metadata.FolderId;
    public bool IsDeleted => metadata.Deleted;
    public NoteMetadata Metadata => metadata;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ModifiedAt { get; private set; }
    public long EditVersion { get; private set; }
    public bool IsClosed { get; private set; }
    public string Title { get => title; set => Edit(ref title, value); }
    public string Text { get => text; set => Edit(ref text, value); }
    public bool Important { get => metadata.Important; set => SetMetadata(metadata with { Important = value }); }
    public bool Favorite { get => metadata.Favorite; set => SetMetadata(metadata with { Favorite = value }); }
    public bool Pinned { get => metadata.Pinned; set => SetMetadata(metadata with { Pinned = value }); }
    public bool Archived { get => metadata.Archived; set => SetMetadata(metadata with { Archived = value }); }
    public string Color { get => metadata.Color; set => SetMetadata(metadata with { Color = value }); }
    private void EnsureEditable()
    {
        if (IsClosed || IsDeleted) throw new InvalidOperationException("Editing session or trash note is closed");
    }
    private void Edit(ref string field, string value, [CallerMemberName] string? property = null)
    {
        EnsureEditable(); ArgumentNullException.ThrowIfNull(value);
        if (field == value) return;
        field = value; Advance(); Notify(property);
    }
    internal void SetMetadata(NoteMetadata value, bool allowTrash = false)
    {
        if (IsClosed || IsDeleted && !allowTrash) throw new InvalidOperationException("Editing session or trash note is closed");
        if (metadata == value) return;
        metadata = value; Advance();
        foreach (var property in new[] { nameof(Metadata), nameof(FolderId), nameof(IsDeleted), nameof(Important), nameof(Favorite), nameof(Pinned), nameof(Archived), nameof(Color) }) Notify(property);
    }
    internal void StageEvent(StoredNote source)
    {
        if (IsClosed) throw new InvalidOperationException("Editing session is closed");
        title = source.Title; text = source.Text; metadata = source.Metadata; ModifiedAt = source.ModifiedAt; EditVersion++;
    }
    internal void PublishEvent()
    {
        foreach (var property in new[] { nameof(Title), nameof(Text), nameof(Metadata), nameof(FolderId), nameof(IsDeleted), nameof(Important), nameof(Favorite), nameof(Pinned), nameof(Archived), nameof(Color), nameof(ModifiedAt), nameof(EditVersion) }) Notify(property);
    }
    private void Advance()
    {
        EditVersion++; ModifiedAt = clock.GetUtcNow(); Notify(nameof(EditVersion)); Notify(nameof(ModifiedAt));
    }
    internal void Close()
    {
        IsClosed = true; title = text = ""; metadata = new();
        Notify(nameof(IsClosed)); Notify(nameof(Title)); Notify(nameof(Text)); Notify(nameof(Metadata));
    }
    private void Notify(string? property) => PropertyChanged?.Invoke(this, new(property));
}

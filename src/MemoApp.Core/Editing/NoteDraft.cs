using System.ComponentModel;
using System.Runtime.CompilerServices;
using MemoApp.Core.Storage;
using MemoApp.Core.Documents;
using System.Collections.Immutable;
namespace MemoApp.Core.Editing;

// Unlocked shared draft. No disk I/O, keys, network or persistent search index.
public sealed class NoteDraft : INotifyPropertyChanged
{
    private readonly TimeProvider clock;
    private string title = "", text = "";
    private string mode="plain";
    private StyledDocument? document;
    private ImmutableArray<Guid> attachmentIds=[];
    private NoteMetadata metadata = new();
    // Inert identity only: consumers may retain this token, never the live draft/event graph.
    internal object AttachmentReadIdentity { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    // Trusted internal revocation only. Never publish UI callbacks from this boundary.
    internal event Action? AttachmentReadInvalidating;
    private void InvalidateAttachmentReads()
    {
        if (AttachmentReadInvalidating is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList()) { try { handler(); } catch { } }
    }
    internal NoteDraft(TimeProvider clock, int order = 0)
    {
        this.clock = clock; metadata = new() { Order = order }; Id = Guid.NewGuid(); CreatedAt = ModifiedAt = clock.GetUtcNow();
    }
    internal NoteDraft(TimeProvider clock, StoredNote source) : this(clock)
    {
        Id = source.NoteId; CreatedAt = source.CreatedAt; ModifiedAt = source.ModifiedAt;
        title = source.Title; text = source.Text; metadata = source.Metadata;mode=source.Mode;document=source.Document;attachmentIds=source.AttachmentIds;
    }
    public Guid Id { get; }
    public Guid? FolderId => metadata.FolderId;
    public bool IsDeleted => metadata.Deleted;
    public NoteMetadata Metadata => metadata;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ModifiedAt { get; private set; }
    public long EditVersion { get; private set; }
    public long ContentVersion { get; private set; }
    public bool IsClosed { get; private set; }
    public string Mode=>mode;
    public StyledDocument? Document=>document;
    public ImmutableArray<Guid> AttachmentIds=>attachmentIds;
    public string Title { get => title; set => Edit(ref title, value); }
    public string Text { get => text; set {if(mode=="rich")throw new InvalidOperationException("Rich content must be edited as a complete document");Edit(ref text, value);} }
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
        InvalidateAttachmentReads();
        field = value;if(property==nameof(Text))ContentVersion++;Advance(); Notify(property);
    }
    internal void SetMetadata(NoteMetadata value, bool allowTrash = false)
    {
        if (IsClosed || IsDeleted && !allowTrash) throw new InvalidOperationException("Editing session or trash note is closed");
        if (metadata == value) return;
        InvalidateAttachmentReads();
        metadata = value; Advance();
        foreach (var property in new[] { nameof(Metadata), nameof(FolderId), nameof(IsDeleted), nameof(Important), nameof(Favorite), nameof(Pinned), nameof(Archived), nameof(Color) }) Notify(property);
    }
    internal void StageEvent(StoredNote source,bool replaceContent=false)
    {
        if (IsClosed) throw new InvalidOperationException("Editing session is closed");
        InvalidateAttachmentReads();
        if(replaceContent||text!=source.Text||mode!=source.Mode||document!=source.Document||!attachmentIds.SequenceEqual(source.AttachmentIds))ContentVersion++;
        title = source.Title; text = source.Text; metadata = source.Metadata;mode=source.Mode;document=source.Document;attachmentIds=source.AttachmentIds; ModifiedAt = source.ModifiedAt; EditVersion++;
    }
    internal void PublishEvent()
    {
        foreach (var property in new[] { nameof(Title), nameof(Text),nameof(Mode),nameof(Document),nameof(AttachmentIds), nameof(Metadata), nameof(FolderId), nameof(IsDeleted), nameof(Important), nameof(Favorite), nameof(Pinned), nameof(Archived), nameof(Color), nameof(ModifiedAt), nameof(EditVersion) }) Notify(property);
    }
    private void Advance()
    {
        EditVersion++; ModifiedAt = clock.GetUtcNow(); Notify(nameof(EditVersion)); Notify(nameof(ModifiedAt));
    }
    internal void StageClose()
    {
        InvalidateAttachmentReads();
        IsClosed = true;ContentVersion++; title = text = ""; metadata = new();mode="plain";document=null;attachmentIds=[];
    }
    internal void Close(){StageClose();PublishClosed();}
    internal void PublishClosed()
    {
        Notify(nameof(IsClosed)); Notify(nameof(Title)); Notify(nameof(Text)); Notify(nameof(Metadata));Notify(nameof(Mode));Notify(nameof(Document));Notify(nameof(AttachmentIds));
    }
    private void Notify(string? property) => PropertyChanged?.Invoke(this, new(property));
}

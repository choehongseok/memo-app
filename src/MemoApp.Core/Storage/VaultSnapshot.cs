using System.Collections.Immutable;
using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
public sealed record NoteMetadata
{
    public Guid? FolderId { get; init; }
    public ImmutableArray<Guid> TagIds { get; init; } = [];
    public string Color { get; init; } = "yellow";
    public bool Important { get; init; }
    public bool Favorite { get; init; }
    public bool Pinned { get; init; }
    public bool Archived { get; init; }
    public bool Deleted { get; init; }
    public int Order { get; init; }
    public ImmutableArray<StoredFilePathLink> FilePathLinks { get; init; }=[];
    public ImmutableArray<StoredAttachmentOcrResult> AttachmentOcrResults {get;init;}=[];
}
public sealed record StoredFolder(Guid FolderId, Guid? ParentId, string Name);
public sealed record StoredTag(Guid TagId, string Name);
public sealed record StoredNote(Guid NoteId, Guid RevisionId, Guid[] Parents, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, string Title, string Text, string Mode = "plain", string Scope = "device-only")
{
    public NoteMetadata Metadata { get; init; } = new();
    public StyledDocument? Document { get; init; }
    public ImmutableArray<Guid> AttachmentIds {get;init;}=[];
}
public sealed record StoredRevision(Guid NoteId, Guid RevisionId, Guid[] Parents, DateTimeOffset ModifiedAt, string Title, string Text)
{
    public NoteMetadata Metadata { get; init; } = new();
    public string Mode { get; init; } = "plain";
    public StyledDocument? Document { get; init; }
    public ImmutableArray<Guid> AttachmentIds {get;init;}=[];
}
public sealed record StoredTombstone(Guid NoteId, Guid RevisionId, Guid[] Parents);
// Contentless causal evidence for discarded notes; never carries source content or attachment references.
public sealed record StoredDiscardedRevision(Guid NoteId, Guid RevisionId, Guid[] Parents);
public sealed record VaultSnapshot(int SchemaVersion, Guid DeviceId, StoredNote[] Notes)
{
    public StoredRevision[] History { get; init; } = [];
    public StoredTombstone[] Tombstones { get; init; } = [];
    public StoredDiscardedRevision[] DiscardedRevisions { get; init; } = [];
    public StoredFolder[] Folders { get; init; } = [];
    public StoredTag[] Tags { get; init; } = [];
    public StoredDeviceUi[] UiDevices { get; init; } = [];
    public Guid AttachmentRootId {get;init;}
    public ImmutableArray<StoredAttachmentObject> AttachmentObjects {get;init;}=[];
}

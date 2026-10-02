namespace MemoApp.Core.Storage;
public sealed record StoredNote(Guid NoteId, Guid RevisionId, Guid[] Parents, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, string Title, string Text, string Mode = "plain", string Scope = "device-only");
public sealed record StoredRevision(Guid NoteId, Guid RevisionId, Guid[] Parents, DateTimeOffset ModifiedAt, string Title, string Text);
public sealed record StoredTombstone(Guid NoteId, Guid RevisionId, Guid[] Parents);
public sealed record VaultSnapshot(int SchemaVersion, Guid DeviceId, StoredNote[] Notes)
{
    public StoredRevision[] History { get; init; } = [];
    public StoredTombstone[] Tombstones { get; init; } = [];
}

using System.Text.Json;
namespace MemoApp.Core.Storage;
internal static class SnapshotValidation
{
    internal static void Json(JsonElement root)
    {
        Fields(root, ["schemaVersion", "deviceId", "notes", "history", "tombstones"]);
        if (!root.GetProperty("schemaVersion").TryGetInt32(out int version) || version is not (1 or 2 or 3)) throw new InvalidDataException("Unsupported schema");
        if (version >= 2) Fields(root, ["folders", "tags"]);
        if(version==3)Fields(root,["uiDevices"]);
        Array(root, "notes", 100); Array(root, "history", 10000); Array(root, "tombstones", 100);
        foreach (var note in root.GetProperty("notes").EnumerateArray())
        {
            Fields(note, ["noteId", "revisionId", "parents", "createdAt", "modifiedAt", "title", "text", "mode", "scope"]);
            if (version >= 2) Metadata(note);
        }
        foreach (var revision in root.GetProperty("history").EnumerateArray())
        {
            Fields(revision, ["noteId", "revisionId", "parents", "modifiedAt", "title", "text"]);
            if (version >= 2) Metadata(revision);
        }
        if(version==3)
        {
            Array(root,"uiDevices",32);
            foreach(var device in root.GetProperty("uiDevices").EnumerateArray())
            {
                Fields(device,["uiDeviceId","preferences","windows"]);var prefs=device.GetProperty("preferences");Fields(prefs,["darkMode","fontSize","scale"]);
                Array(device,"windows",102);
                foreach(var window in device.GetProperty("windows").EnumerateArray())Fields(window,["kind","noteId","monitor","x","y","width","height","dpi","open","topmost","opacity","folded","positionLocked"]);
            }
        }
        foreach (var tombstone in root.GetProperty("tombstones").EnumerateArray()) Fields(tombstone, ["noteId", "revisionId", "parents"]);
        if (version >= 2)
        {
            Array(root, "folders", 100); Array(root, "tags", 100);
            foreach (var folder in root.GetProperty("folders").EnumerateArray()) Fields(folder, ["folderId", "parentId", "name"]);
            foreach (var tag in root.GetProperty("tags").EnumerateArray()) Fields(tag, ["tagId", "name"]);
        }
    }
    private static void Fields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object || fields.Any(f => !element.TryGetProperty(f, out _))) throw new InvalidDataException("Missing schema fields");
    }
    private static void Array(JsonElement element, string field, int max)
    {
        var value = element.GetProperty(field);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > max) throw new InvalidDataException("Array size/type outside limits");
    }
    private static void Metadata(JsonElement record)
    {
        Fields(record, ["metadata"]); var metadata = record.GetProperty("metadata");
        Fields(metadata, ["folderId", "tagIds", "color", "important", "favorite", "pinned", "archived", "deleted", "order"]);
        Array(metadata, "tagIds", 16);
        foreach (var name in new[] { "important", "favorite", "pinned", "archived", "deleted" })
            if (metadata.GetProperty(name).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Invalid metadata flag");
    }
    internal static void Validate(VaultSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not (1 or 2 or 3) || snapshot.DeviceId == Guid.Empty || snapshot.Notes is null || snapshot.Notes.Length > 100 || snapshot.History is null || snapshot.History.Length > 10000 || snapshot.Tombstones is null || snapshot.Tombstones.Length > 100 || snapshot.Folders is null || snapshot.Tags is null || snapshot.Folders.Length > 100 || snapshot.Tags.Length > 100 || snapshot.UiDevices is null || snapshot.UiDevices.Length>32) throw new InvalidDataException("Unsupported snapshot or record limit");
        bool legacy = snapshot.SchemaVersion == 1;
        if(snapshot.SchemaVersion<3&&snapshot.UiDevices.Length!=0)throw new InvalidDataException("Older payload cannot carry UI records");
        if (legacy && (snapshot.Folders.Length != 0 || snapshot.Tags.Length != 0)) throw new InvalidDataException("Legacy schema cannot carry organization");
        var folderMap = new Dictionary<Guid, StoredFolder>();
        foreach (var folder in snapshot.Folders)
            if (folder is null || folder.FolderId == Guid.Empty || !folderMap.TryAdd(folder.FolderId, folder) || !Name(folder.Name) || folder.ParentId == Guid.Empty) throw new InvalidDataException("Invalid folder");
        foreach (var folder in snapshot.Folders)
        {
            var visited = new HashSet<Guid> { folder.FolderId }; var parent = folder.ParentId;
            while (parent is Guid id)
            {
                if (!folderMap.TryGetValue(id, out var ancestor) || !visited.Add(id)) throw new InvalidDataException("Folder parent/cycle invalid");
                parent = ancestor.ParentId;
            }
        }
        if (snapshot.Folders.GroupBy(f => f.ParentId).Any(g => g.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != g.Count())) throw new InvalidDataException("Duplicate sibling folder name");
        var tagIds = new HashSet<Guid>(); var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in snapshot.Tags)
            if (tag is null || tag.TagId == Guid.Empty || !tagIds.Add(tag.TagId) || !Name(tag.Name) || !tagNames.Add(tag.Name)) throw new InvalidDataException("Invalid tag");
        void Meta(NoteMetadata metadata)
        {
            if (metadata is null || metadata.TagIds.IsDefault || metadata.TagIds.Length > 16 || metadata.TagIds.Distinct().Count() != metadata.TagIds.Length || metadata.TagIds.Any(t => !tagIds.Contains(t)) || metadata.FolderId is Guid f && !folderMap.ContainsKey(f) || metadata.Color is not ("yellow" or "blue" or "green" or "pink" or "white" or "purple") || metadata.Order is < 0 or > 1000000) throw new InvalidDataException("Invalid organization metadata");
            if (legacy && (metadata.FolderId is not null || metadata.TagIds.Length != 0 || metadata.Color != "yellow" || metadata.Important || metadata.Favorite || metadata.Pinned || metadata.Archived || metadata.Deleted || metadata.Order != 0)) throw new InvalidDataException("Legacy metadata not supported");
        }
        var ids = new HashSet<Guid>(); var revisions = new HashSet<Guid>(); var graph = new Dictionary<Guid, (Guid NoteId, Guid[] Parents)>();
        bool Parents(Guid[] parents, Guid revision) => parents is not null && parents.Length <= 8 && parents.Distinct().Count() == parents.Length && !parents.Any(p => p == Guid.Empty || p == revision);
        foreach (var note in snapshot.Notes)
        {
            if (note is null || note.NoteId == Guid.Empty || !ids.Add(note.NoteId) || note.RevisionId == Guid.Empty || !revisions.Add(note.RevisionId) || note.Title is null || note.Text is null || note.Title.Length > 256 || note.Text.Length > 65536 || !Parents(note.Parents, note.RevisionId) || note.Mode != "plain" || note.Scope != "device-only" || note.CreatedAt.Offset != TimeSpan.Zero || note.ModifiedAt.Offset != TimeSpan.Zero) throw new InvalidDataException("Invalid note fields or unsupported mode");
            Meta(note.Metadata); graph.Add(note.RevisionId, (note.NoteId, note.Parents));
        }
        foreach (var revision in snapshot.History)
        {
            if (revision is null || !ids.Contains(revision.NoteId) || revision.RevisionId == Guid.Empty || !revisions.Add(revision.RevisionId) || !Parents(revision.Parents, revision.RevisionId) || revision.Title is null || revision.Title.Length > 256 || revision.Text is null || revision.Text.Length > 65536 || revision.ModifiedAt.Offset != TimeSpan.Zero) throw new InvalidDataException("Invalid history");
            Meta(revision.Metadata); graph.Add(revision.RevisionId, (revision.NoteId, revision.Parents));
        }
        if (snapshot.History.GroupBy(r => r.NoteId).Any(g => g.Count() > 512)) throw new InvalidDataException("History limit exceeded");
        var deletedIds = new HashSet<Guid>();
        foreach (var deleted in snapshot.Tombstones)
        {
            if (deleted is null || deleted.NoteId == Guid.Empty || !deletedIds.Add(deleted.NoteId) || deleted.RevisionId == Guid.Empty || !Parents(deleted.Parents, deleted.RevisionId)) throw new InvalidDataException("Invalid tombstone");
            var retained = snapshot.Notes.FirstOrDefault(n => n.NoteId == deleted.NoteId);
            if (retained is not null)
            {
                if (legacy || !retained.Metadata.Deleted || retained.RevisionId != deleted.RevisionId || !retained.Parents.SequenceEqual(deleted.Parents)) throw new InvalidDataException("Live/deleted tombstone mismatch");
            }
            else if (!revisions.Add(deleted.RevisionId) || deleted.Parents.Length != 0) throw new InvalidDataException("Invalid legacy contentless tombstone");
        }
        if (snapshot.Notes.Any(n => n.Metadata.Deleted && !deletedIds.Contains(n.NoteId))) throw new InvalidDataException("Deleted note missing tombstone");
        foreach (var pair in graph)
            if (pair.Value.Parents.Any(p => !graph.TryGetValue(p, out var parent) || parent.NoteId != pair.Value.NoteId)) throw new InvalidDataException("Invalid revision relation");
        var visitedRevisions = new HashSet<Guid>(); var visiting = new HashSet<Guid>();
        void Visit(Guid revision)
        {
            if (visitedRevisions.Contains(revision)) return;
            if (!visiting.Add(revision)) throw new InvalidDataException("Revision cycle");
            foreach (var parent in graph[revision].Parents) Visit(parent);
            visiting.Remove(revision); visitedRevisions.Add(revision);
        }
        foreach (var revision in graph.Keys) Visit(revision);
        var deviceIds=new HashSet<Guid>();
        foreach(var device in snapshot.UiDevices)
        {
            if(device is null||device.UiDeviceId==Guid.Empty||!deviceIds.Add(device.UiDeviceId)||device.Preferences is null||device.Windows.IsDefault||device.Windows.Length>102||!double.IsFinite(device.Preferences.FontSize)||device.Preferences.FontSize is <12 or >36||!double.IsFinite(device.Preferences.Scale)||device.Preferences.Scale is <0.75 or >1.75)throw new InvalidDataException("Invalid UI profile/preferences");
            var windowIds=new HashSet<(string,Guid?)>();
            foreach(var window in device.Windows)
            {
                if(window is null||window.Kind is not ("memo" or "calendar" or "clock")||!windowIds.Add((window.Kind,window.NoteId))||window.Kind=="memo"&&(window.NoteId is not Guid noteId||!ids.Contains(noteId))||window.Kind!="memo"&&window.NoteId is not null||window.Monitor is null||window.Monitor.Length is <1 or >128||window.Monitor.Any(char.IsControl)||!double.IsFinite(window.X)||window.X is <0 or >1||!double.IsFinite(window.Y)||window.Y is <0 or >1||!double.IsFinite(window.Width)||window.Width is <200 or >2000||!double.IsFinite(window.Height)||window.Height is <100 or >1600||!double.IsFinite(window.Dpi)||window.Dpi is <48 or >768||!double.IsFinite(window.Opacity)||window.Opacity is <0.3 or >1)throw new InvalidDataException("Invalid UI window fields/reference");
            }
        }
        // Event/import preflight must reject the same payload budget as Prepare before publishing any draft.
        using var counter = new PayloadCounter(VaultEnvelope.MaxFile - VaultEnvelope.HeaderSize - 148);
        JsonSerializer.Serialize(counter, snapshot, VaultEnvelope.JsonOptions);
    }
    private sealed class PayloadCounter(int limit) : Stream
    {
        private long count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => count;
        public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int size) => Write(buffer.AsSpan(offset, size));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > limit - count) throw new InvalidDataException("Snapshot payload byte budget exceeded");
            count += buffer.Length;
        }
        public override int Read(byte[] buffer, int offset, int size) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
    }
    private static bool Name(string value) => value is not null && value.Length is > 0 and <= 128 && value.Trim() == value && !value.Any(char.IsControl);
}

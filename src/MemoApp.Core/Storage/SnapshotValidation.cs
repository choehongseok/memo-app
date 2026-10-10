using System.Text.Json;
using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
internal static class SnapshotValidation
{
    internal static void Json(JsonElement root)
    {
        Fields(root, ["schemaVersion", "deviceId", "notes", "history", "tombstones"]);
        if (!root.GetProperty("schemaVersion").TryGetInt32(out int version) || version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10)) throw new InvalidDataException("Unsupported schema");
        if(version<9&&root.TryGetProperty("uiDevices",out var legacyDevices)&&legacyDevices.ValueKind==JsonValueKind.Array)
            foreach(var device in legacyDevices.EnumerateArray())if(device.ValueKind==JsonValueKind.Object&&device.TryGetProperty("automaticBackupPolicy",out _))throw new InvalidDataException("Older schema backup policy field refused");
        if(version<8&&root.TryGetProperty("discardedRevisions",out _))throw new InvalidDataException("Older schema discarded evidence refused");
        if(version>=8)
        {
            Fields(root,["discardedRevisions"]);Array(root,"discardedRevisions",10100);
            foreach(var witness in root.GetProperty("discardedRevisions").EnumerateArray())
            {Fields(witness,["noteId","revisionId","parents"]);if(witness.EnumerateObject().Count()!=3)throw new InvalidDataException("Content in discarded evidence refused");Array(witness,"parents",8);}
        }
        if (version >= 2) Fields(root, ["folders", "tags"]);
        if(version>=3)Fields(root,["uiDevices"]);
        if(version>=5)
        {
            Fields(root,["attachmentRootId","attachmentObjects"]);Array(root,"attachmentObjects",128);
            foreach(var item in root.GetProperty("attachmentObjects").EnumerateArray()){Fields(item,["objectId","rootId","name","mime","length","sha256","wrappedKey","chunks"]);Array(item,"chunks",64);}
        }
        Array(root, "notes", 100); Array(root, "history", 10000); Array(root, "tombstones", 100);
        foreach (var note in root.GetProperty("notes").EnumerateArray())
        {
            Fields(note, ["noteId", "revisionId", "parents", "createdAt", "modifiedAt", "title", "text", "mode", "scope"]);
            Metadata(note,version);
            if(version>=4)Document(note,false);
            if(version>=5){Fields(note,["attachmentIds"]);Array(note,"attachmentIds",16);}
        }
        foreach (var revision in root.GetProperty("history").EnumerateArray())
        {
            Fields(revision, ["noteId", "revisionId", "parents", "modifiedAt", "title", "text"]);
            Metadata(revision,version);
            if(version>=4)Document(revision,true);
            if(version>=5){Fields(revision,["attachmentIds"]);Array(revision,"attachmentIds",16);}
        }
        if(version>=3)
        {
            Array(root,"uiDevices",32);
            foreach(var device in root.GetProperty("uiDevices").EnumerateArray())
            {
                Fields(device,["uiDeviceId","preferences","windows"]);var prefs=device.GetProperty("preferences");Fields(prefs,["darkMode","fontSize","scale"]);
                if(version<6&&(device.TryGetProperty("recentNoteIds",out _)||device.TryGetProperty("savedSearches",out _)))throw new InvalidDataException("Older schema cannot carry search UI state");
                if(version>=6)
                {
                    Fields(device,["recentNoteIds","savedSearches"]);Array(device,"recentNoteIds",20);Array(device,"savedSearches",20);
                    foreach(var saved in device.GetProperty("savedSearches").EnumerateArray())
                    {Fields(saved,["id","name","options"]);Fields(saved.GetProperty("options"),["query","field","view","sort","folderId","unfiledOnly","includeDescendants","tag","favoriteOnly","importantOnly","modifiedFrom","modifiedUntil"]);}
                }
                if(version<9&&device.TryGetProperty("automaticBackupPolicy",out _))throw new InvalidDataException("Older schema backup policy refused");
                if(version>=9)
                {
                    Fields(device,["automaticBackupPolicy"]);var policy=device.GetProperty("automaticBackupPolicy");
                    if(policy.ValueKind!=JsonValueKind.Null)
                    {
                        Fields(policy,["directory","vaultIdentity","sourceRootBinding","capacity","afterSave","daily","onExit","lastAttemptDay"]);
                        if(policy.EnumerateObject().Count()!=8)throw new InvalidDataException("Unknown backup policy fields");
                        var binding=policy.GetProperty("sourceRootBinding");Fields(binding,["rootPath","volumeSerialNumber","directoryFileId"]);
                        if(binding.EnumerateObject().Count()!=3||!binding.GetProperty("volumeSerialNumber").TryGetUInt64(out _))throw new InvalidDataException("Source binding identity type/fields");
                        foreach(string flag in new[]{"afterSave","daily","onExit"})if(policy.GetProperty(flag).ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Invalid backup trigger type");
                        var day=policy.GetProperty("lastAttemptDay");if(day.ValueKind!=JsonValueKind.Null&&(day.ValueKind!=JsonValueKind.String||day.GetString() is not string text||text.Length!=10||!DateOnly.TryParseExact(text,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _)))throw new InvalidDataException("Invalid UTC attempt day");
                    }
                }
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
    private static void Document(JsonElement record,bool revision)
    {
        Fields(record,revision?["mode","document"]:["document"]);var document=record.GetProperty("document");
        if(document.ValueKind!=JsonValueKind.Null)Fields(document,["schemaVersion","sourceJson"]);
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
    private static void Metadata(JsonElement record,int version)
    {
        if(version==1){if(record.TryGetProperty("metadata",out var old)&&old.TryGetProperty("filePathLinks",out _))throw new InvalidDataException("Older schema file paths refused");return;}
        Fields(record, ["metadata"]); var metadata = record.GetProperty("metadata");
        if(version<7&&metadata.TryGetProperty("filePathLinks",out _))throw new InvalidDataException("Older schema file paths refused");
        if(version>=7){Fields(metadata,["filePathLinks"]);Array(metadata,"filePathLinks",16);foreach(var link in metadata.GetProperty("filePathLinks").EnumerateArray())Fields(link,["id","uiDeviceId","name","path"]);}
        Fields(metadata, ["folderId", "tagIds", "color", "important", "favorite", "pinned", "archived", "deleted", "order"]);
        Array(metadata, "tagIds", 16);
        foreach (var name in new[] { "important", "favorite", "pinned", "archived", "deleted" })
            if (metadata.GetProperty(name).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Invalid metadata flag");
    }
    internal static void Validate(VaultSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10) || snapshot.DeviceId == Guid.Empty || snapshot.Notes is null || snapshot.Notes.Length > 100 || snapshot.History is null || snapshot.History.Length > 10000 || snapshot.Tombstones is null || snapshot.Tombstones.Length>100 || snapshot.Folders is null || snapshot.Tags is null || snapshot.Folders.Length > 100 || snapshot.Tags.Length > 100 || snapshot.UiDevices is null || snapshot.UiDevices.Length>32) throw new InvalidDataException("Unsupported snapshot or record limit");
        if(snapshot.DiscardedRevisions is null||snapshot.DiscardedRevisions.Length>10100||snapshot.DiscardedRevisions.Length+snapshot.History.Length>10100||snapshot.SchemaVersion<8&&snapshot.DiscardedRevisions.Length!=0)throw new InvalidDataException("Discarded evidence count/schema limit");
        var attachmentObjects=AttachmentValidation.Objects(snapshot);
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
            FilePathLinkValidation.Links(metadata.FilePathLinks,snapshot.SchemaVersion);
            if (legacy && (metadata.FolderId is not null || metadata.TagIds.Length != 0 || metadata.Color != "yellow" || metadata.Important || metadata.Favorite || metadata.Pinned || metadata.Archived || metadata.Deleted || metadata.Order != 0)) throw new InvalidDataException("Legacy metadata not supported");
        }
        void Content(string mode,StyledDocument? document,string text,System.Collections.Immutable.ImmutableArray<Guid> references)
        {
            if(!RichDocumentCodec.IsWellFormedUnicode(text)||mode is not ("plain" or "markdown" or "rich")||snapshot.SchemaVersion<4&&(mode!="plain"||document is not null))throw new InvalidDataException("Invalid document mode/Unicode/version");
            if(mode=="rich")
            {
                if(document is null)throw new InvalidDataException("Rich source required");var info=RichDocumentCodec.Inspect(document);
                AttachmentValidation.Document(document,snapshot.SchemaVersion,references,snapshot.AttachmentObjects);
                if(info.Supported&&!string.Equals(info.Text,text,StringComparison.Ordinal))throw new InvalidDataException("Rich projection/source mismatch");
            }
            else if(document is not null)throw new InvalidDataException("Plain/Markdown source must not carry rich data");
        }
        var ids = new HashSet<Guid>(); var revisions = new HashSet<Guid>(); var graph = new Dictionary<Guid, (Guid NoteId, Guid[] Parents)>();
        bool Parents(Guid[] parents, Guid revision) => parents is not null && parents.Length <= 8 && parents.Distinct().Count() == parents.Length && !parents.Any(p => p == Guid.Empty || p == revision);
        foreach (var note in snapshot.Notes)
        {
            if (note is null || note.NoteId == Guid.Empty || !ids.Add(note.NoteId) || note.RevisionId == Guid.Empty || !revisions.Add(note.RevisionId) || note.Title is null || note.Text is null || note.Title.Length > 256 || !RichDocumentCodec.IsWellFormedUnicode(note.Title) || note.Text.Length > 65536 || !Parents(note.Parents, note.RevisionId) || note.Scope != "device-only" || note.CreatedAt.Offset != TimeSpan.Zero || note.ModifiedAt.Offset != TimeSpan.Zero) throw new InvalidDataException("Invalid note fields or unsupported mode");
            Content(note.Mode,note.Document,note.Text,note.AttachmentIds);AttachmentValidation.References(note.AttachmentIds,attachmentObjects,snapshot.SchemaVersion);Meta(note.Metadata); graph.Add(note.RevisionId, (note.NoteId, note.Parents));
        }
        foreach (var revision in snapshot.History)
        {
            if (revision is null || !ids.Contains(revision.NoteId) || revision.RevisionId == Guid.Empty || !revisions.Add(revision.RevisionId) || !Parents(revision.Parents, revision.RevisionId) || revision.Title is null || revision.Title.Length > 256 || !RichDocumentCodec.IsWellFormedUnicode(revision.Title) || revision.Text is null || revision.Text.Length > 65536 || revision.ModifiedAt.Offset != TimeSpan.Zero) throw new InvalidDataException("Invalid history");
            Content(revision.Mode,revision.Document,revision.Text,revision.AttachmentIds);AttachmentValidation.References(revision.AttachmentIds,attachmentObjects,snapshot.SchemaVersion);Meta(revision.Metadata); graph.Add(revision.RevisionId, (revision.NoteId, revision.Parents));
        }
        if (snapshot.History.GroupBy(r => r.NoteId).Any(g => g.Count() > 512)) throw new InvalidDataException("History limit exceeded");
        var discardedIds=new HashSet<Guid>();
        foreach(var witness in snapshot.DiscardedRevisions)
        {
            if(witness is null||witness.NoteId==Guid.Empty||ids.Contains(witness.NoteId)||!snapshot.Tombstones.Any(t=>t is not null&&t.NoteId==witness.NoteId)||witness.RevisionId==Guid.Empty||!revisions.Add(witness.RevisionId)||!Parents(witness.Parents,witness.RevisionId))throw new InvalidDataException("Invalid discarded evidence");
            discardedIds.Add(witness.NoteId);graph.Add(witness.RevisionId,(witness.NoteId,witness.Parents));
        }
        if(snapshot.DiscardedRevisions.GroupBy(r=>r.NoteId).Any(g=>g.Count()>513))throw new InvalidDataException("Discarded note revision limit");
        var deletedIds = new HashSet<Guid>();
        foreach (var deleted in snapshot.Tombstones)
        {
            if (deleted is null || deleted.NoteId == Guid.Empty || !deletedIds.Add(deleted.NoteId) || deleted.RevisionId == Guid.Empty || !Parents(deleted.Parents, deleted.RevisionId)) throw new InvalidDataException("Invalid tombstone");
            var retained = snapshot.Notes.FirstOrDefault(n => n.NoteId == deleted.NoteId);
            if (retained is not null)
            {
                if (legacy || !retained.Metadata.Deleted || retained.RevisionId != deleted.RevisionId || !retained.Parents.SequenceEqual(deleted.Parents)) throw new InvalidDataException("Live/deleted tombstone mismatch");
            }
            else if(discardedIds.Contains(deleted.NoteId))
            {
                if(!graph.TryGetValue(deleted.RevisionId,out var witness)||witness.NoteId!=deleted.NoteId||!witness.Parents.SequenceEqual(deleted.Parents))throw new InvalidDataException("Discarded tombstone original identity/parents mismatch");
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
            DeviceSearchValidation.Validate(snapshot,device);
            AutomaticBackupPolicyValidation.Validate(device.AutomaticBackupPolicy,snapshot.SchemaVersion);
            var windowIds=new HashSet<(string,Guid?)>();
            foreach(var window in device.Windows)
            {
                if(window is null||window.Kind is not ("memo" or "calendar" or "clock")||!windowIds.Add((window.Kind,window.NoteId))||window.Kind=="memo"&&(window.NoteId is not Guid noteId||!ids.Contains(noteId))||window.Kind!="memo"&&window.NoteId is not null||window.Monitor is null||window.Monitor.Length is <1 or >128||window.Monitor.Any(char.IsControl)||!RichDocumentCodec.IsWellFormedUnicode(window.Monitor)||!double.IsFinite(window.X)||window.X is <0 or >1||!double.IsFinite(window.Y)||window.Y is <0 or >1||!double.IsFinite(window.Width)||window.Width is <200 or >2000||!double.IsFinite(window.Height)||window.Height is <100 or >1600||!double.IsFinite(window.Dpi)||window.Dpi is <48 or >768||!double.IsFinite(window.Opacity)||window.Opacity is <0.3 or >1)throw new InvalidDataException("Invalid UI window fields/reference");
            }
        }
        // Event/import preflight must reject the same payload budget as Prepare before publishing any draft.
        using var counter = new PayloadCounter(VaultEnvelope.MaxFile - (snapshot.SchemaVersion>=5?308:232));
        SnapshotSerialization.Write(counter,snapshot);
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
    private static bool Name(string value) => value is not null && value.Length is > 0 and <= 128 && value.Trim() == value && !value.Any(char.IsControl)&&RichDocumentCodec.IsWellFormedUnicode(value);
}

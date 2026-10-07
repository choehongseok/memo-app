using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class OrganizationChecks
{
    internal static void Run()
    {
        var workspace = new EditingWorkspace(TimeProvider.System);
        var folder = workspace.CreateFolder("합성 상위");
        var child = workspace.CreateFolder("합성 하위", folder.FolderId);
        var first = workspace.CreateNote(); first.Title = "합성 조직 제목"; first.Text = "합성 최초 본문";
        var second = workspace.CreateNote(); second.Text = "비선택 자료";
        workspace.MoveNote(first, child.FolderId);
        workspace.SetTags(first, ["테스트", "합성", "테스트"]);
        VaultChecks.Require(first.FolderId == child.FolderId && first.Metadata.TagIds.Length == 2 && workspace.Tags.Count == 2, "folder and unique tag IDs");
        VaultChecks.ExpectFailure(() => workspace.MoveNote(first, Guid.NewGuid()), "unknown folder must reject move");
        VaultChecks.ExpectFailure(() => workspace.CreateFolder("bad", Guid.NewGuid()), "unknown folder parent must reject");
        var saved = workspace.Capture(); workspace.AcceptPrepared(saved);
        first.Text = "합성 최신 본문";
        var edited = workspace.Capture(); workspace.AcceptPrepared(edited);
        VaultChecks.Require(edited.SchemaVersion == 2 && edited.History.Single().Text == "합성 최초 본문", "v2 revision capture preserves original");
        workspace.SetImportant(first, true);
        var metadataEdit = workspace.Capture(); workspace.AcceptPrepared(metadataEdit);
        VaultChecks.Require(metadataEdit.History.Length == 2 && metadataEdit.Notes[0].Metadata.Important, "metadata changes must create history");
        workspace.DeleteNote(first);
        VaultChecks.ExpectFailure(() => first.Text = "should reject", "trash note editor must reject direct edits");
        var deleted = workspace.Capture(); workspace.AcceptPrepared(deleted);
        VaultChecks.Require(first.IsDeleted && deleted.Tombstones.Single().RevisionId == deleted.Notes[0].RevisionId && deleted.History.Length == 3, "delete retains note content and current tombstone");
        workspace.RestoreNote(first);
        var restored = workspace.Capture(); workspace.AcceptPrepared(restored);
        VaultChecks.Require(!first.IsDeleted && first.Text == "합성 최신 본문" && restored.Tombstones.Length == 0 && restored.History.Last().Metadata.Deleted, "restore must retain immutable deletion history");
        workspace.RestoreRevision(first, saved.Notes[0].RevisionId);
        var reverted = workspace.Capture(); workspace.AcceptPrepared(reverted);
        VaultChecks.Require(first.Text == "합성 최초 본문" && reverted.Notes[0].Parents.Single() == restored.Notes[0].RevisionId && second.Text == "비선택 자료", "history restore must create fresh current-head descendant and preserve others");
        workspace.DeleteNote(first); workspace.RestoreNote(first);
        var rapid = workspace.Capture();
        VaultChecks.Require(rapid.History.Any(h => h.NoteId == first.Id && h.Metadata.Deleted && h.RevisionId != restored.History.Last().RevisionId), "rapid delete/restore before autosave must retain deletion event");
        workspace.AcceptPrepared(rapid);
        var copy = workspace.Duplicate(first);
        VaultChecks.Require(copy.Id != first.Id && copy.Text == first.Text && copy.FolderId == first.FolderId && !copy.IsDeleted, "duplicate creates independent complete draft");
        copy.Text = "독립 사본";
        VaultChecks.Require(first.Text == "합성 최초 본문", "copy editing must be independent");
        var ordered = new EditingWorkspace(TimeProvider.System); var a = ordered.CreateNote(); a.Title = "a"; var b = ordered.CreateNote(); b.Title = "b"; var c = ordered.CreateNote(); c.Title = "c";
        ordered.AcceptPrepared(ordered.Capture()); ordered.ReorderBefore(c, a);
        VaultChecks.Require(MemoApp.Core.Search.NoteSearch.Find(ordered, new() { Sort = MemoApp.Core.Search.SearchSort.Custom }).SequenceEqual([c,a,b]), "custom reorder before must change actual order");
        var reordered = ordered.Capture(); ordered.AcceptPrepared(reordered); var same = reordered.Notes.Select(n => n.RevisionId).ToArray();
        ordered.ReorderBefore(c,a); VaultChecks.Require(ordered.Capture().Notes.Select(n => n.RevisionId).SequenceEqual(same), "no-op reorder must not create history");
        VaultChecks.ExpectFailure(() => ordered.ReorderBefore(a, first), "foreign target must reject");
        c.Pinned=true; var pinnedBefore=ordered.Capture(); ordered.AcceptPrepared(pinnedBefore);
        VaultChecks.ExpectFailure(()=>ordered.ReorderBefore(a,c),"list-pinned groups must not be moved together");
        VaultChecks.Require(System.Text.Json.JsonSerializer.Serialize(ordered.Capture())==System.Text.Json.JsonSerializer.Serialize(pinnedBefore),"pinned rejection changed hidden data");
        var eventClose=new EditingWorkspace(TimeProvider.System); var closeA=eventClose.CreateNote(); var closeB=eventClose.CreateNote(); eventClose.AcceptPrepared(eventClose.Capture()); bool cleared=false;
        closeA.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.Title)&&!cleared){cleared=true;eventClose.Clear();}};
        eventClose.ReorderBefore(closeB,closeA);
        VaultChecks.Require(eventClose.Notes.Count==0 && eventClose.FrozenBasis.Notes.Length==0,"batch public notification close must not enumerate/reinject plaintext after clear");
        var retained = first; workspace.Clear();
        VaultChecks.Require(workspace.Folders.Count == 0 && workspace.Tags.Count == 0 && retained.IsClosed, "clear removes organization and closes retained references");
        VaultChecks.ExpectFailure(() => workspace.CreateFolder("after lock"), "closed workspace rejects new edits");
        foreach (bool deletedAtLimit in new[] { false, true })
        {
            var id = Guid.NewGuid(); var revisionIds = Enumerable.Range(0, 513).Select(_ => Guid.NewGuid()).ToArray(); var now = DateTimeOffset.UtcNow;
            var fullHistory = Enumerable.Range(0, 512).Select(i => new StoredRevision(id, revisionIds[i], i == 0 ? [] : [revisionIds[i-1]], now, $"old {i}", "old body")).ToArray();
            var head = new StoredNote(id, revisionIds[512], [revisionIds[511]], now, now, "current", "current body") { Metadata = new() { Deleted = deletedAtLimit } };
            var full = new VaultSnapshot(2, Guid.NewGuid(), [head]) { History = fullHistory, Tombstones = deletedAtLimit ? [new(id, head.RevisionId, head.Parents)] : [] };
            var limited = new EditingWorkspace(TimeProvider.System, full); var limitedNote = limited.Notes.Single();
            if (deletedAtLimit) VaultChecks.ExpectFailure(() => limited.RestoreNote(limitedNote), "full-history restore must reject");
            else
            {
                VaultChecks.ExpectFailure(() => limited.DeleteNote(limitedNote), "full-history delete must reject");
                VaultChecks.ExpectFailure(() => limited.RestoreRevision(limitedNote, fullHistory[0].RevisionId), "full-history version restore must reject");
            }
            var other = limited.CreateNote(); other.Title = "other"; var beforeOrder = limited.Capture(); limited.AcceptPrepared(beforeOrder);
            if (!deletedAtLimit) VaultChecks.ExpectFailure(() => limited.ReorderBefore(other, limitedNote), "reorder history overflow rejects entire batch");
            var after = limited.Capture();
            VaultChecks.Require(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(after).SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(beforeOrder)),"rejected order must preserve every note/head/history/metadata");
            VaultChecks.Require(limitedNote.IsDeleted == deletedAtLimit && limitedNote.Title == "current" && limitedNote.Text == "current body" && after.Notes[0].RevisionId == head.RevisionId && after.History.Length == 512 && limitedNote.EditVersion == 0 && after.Notes[0].Metadata.Order == head.Metadata.Order, "rejected event must not mutate draft/basis/version/history");
        }
        Console.WriteLine("PASS: folders/tags, metadata history, trash/new-revision restore, immutable originals, duplication and close");
    }
}

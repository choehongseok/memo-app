using MemoApp.Core.Editing;
using MemoApp.Core.Search;
internal static class SearchChecks
{
    internal static void Run()
    {
        var workspace = new EditingWorkspace(TimeProvider.System);
        var first = workspace.CreateNote(); first.Title = "합성 Café ABC"; first.Text = "본문 전용 바나나";
        var second = workspace.CreateNote(); second.Title = "두번째"; second.Text = "합성 제목 아님";
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "abc", SearchField.Title).SequenceEqual([first]), "case insensitive title search");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "cafe\u0301").SequenceEqual([first]), "NFC normalization search");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "바나나", SearchField.Body).SequenceEqual([first]), "body-only search");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "바나나", SearchField.Title).Length == 0, "title filter must not match body");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "합성").Length == 2, "all-fields literal search");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "").Length == 2, "empty search lists unlocked notes");
        VaultChecks.Require(NoteSearch.Find(workspace.Notes, "[.*").Length == 0, "query must not execute regular expressions");
        var folder = workspace.CreateFolder("검색 상위"); var child = workspace.CreateFolder("검색 하위", folder.FolderId);
        workspace.MoveNote(first, child.FolderId); workspace.SetTags(first, ["한글태그"]); first.Favorite = true; first.Important = true;
        var archived = workspace.CreateNote(); archived.Title = "보관 합성"; archived.Archived = true;
        var trash = workspace.CreateNote(); trash.Title = "휴지통 합성"; workspace.DeleteNote(trash);
        VaultChecks.Require(NoteSearch.Find(workspace, new()).Length == 2, "default active view must exclude archive/trash");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { FolderId = folder.FolderId }).SequenceEqual([first]), "folder subtree search");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { FolderId = folder.FolderId, IncludeDescendants = false }).Length == 0, "exact parent folder search");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { Tag = "한글태그", FavoriteOnly = true, ImportantOnly = true, Query = "abc" }).SequenceEqual([first]), "combined tag/favorite/important/text search");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { View = SearchView.Archive }).SequenceEqual([archived]), "archive view");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { View = SearchView.Trash }).SequenceEqual([trash]), "trash explicit view");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { ModifiedFrom = DateTimeOffset.UtcNow.AddDays(1) }).Length == 0, "date filtering");
        VaultChecks.Require(NoteSearch.Find(workspace, new() { UnfiledOnly = true }).SequenceEqual([second]), "unfiled filter");
        var retained = workspace.Notes.ToArray(); workspace.Clear();
        VaultChecks.Require(NoteSearch.Find(retained, "").Length == 0, "retained closed drafts must be excluded");
        Console.WriteLine("PASS: real literal title/body/all NFC/case search and closed-draft exclusion");
    }
}

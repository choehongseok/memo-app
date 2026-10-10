using MemoApp.Core.Editing;

internal static class EditingChecks
{
    public static void Run()
    {
        var time = new ManualClock();
        var workspace = new EditingWorkspace(time);
        var first = workspace.CreateNote();
        var second = workspace.CreateNote();
        Require(workspace.Notes.Count == 2 && first.Id != second.Id && first.Id != Guid.Empty,
            "new notes must have distinct identity and appear in list");
        Require(first.CreatedAt == time.Now && first.ModifiedAt == time.Now,
            "new note timestamps must use injected UTC clock");
        var sharedEditor = workspace.Notes[0];
        var events = new List<string?>();
        sharedEditor.PropertyChanged += (_, e) => events.Add(e.PropertyName);
        time.Now = time.Now.AddMinutes(1);
        first.Title = "합성 제목";
        first.Text = "합성 한글 본문";
        Require(sharedEditor.Title == "합성 제목" && sharedEditor.Text == "합성 한글 본문",
            "list and sticky editors must share draft edits");
        Require(first.EditVersion == 2 && first.ModifiedAt == time.Now,
            "each real edit must advance version and modification timestamp");
        Require(events.Contains("Title") && events.Contains("Text") && events.Contains("EditVersion"),
            "bound editors must receive change notifications");
        first.Text = first.Text;
        Require(first.EditVersion == 2, "same value must not create another edit");
        Require(second.Title == "" && second.Text == "", "one note edit must not change another");
        workspace.Clear();
        Require(workspace.Notes.Count == 0 && first.Title == "" && first.Text == "" && first.IsClosed,
            "session clear must empty bound drafts and revoke retained editor references");
        try { first.Text = "사용 금지"; throw new Exception("closed draft allowed edits"); }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS: real in-memory editing, multi-editor notifications, timestamps, close/revocation");
        Console.WriteLine("LIMIT: editing checks do not prove encryption, disk autosave or restart restore");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

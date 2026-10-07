using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        int result = 1; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Run(); Console.WriteLine("PASS: actual Windows WPF control construction/layout, bound editing/search/folders/trash/history/sticky and lock/undo clearing (not IME/OS SessionLock/user usability)"); result = 0; }
            catch (Exception e) { var actual = e.GetBaseException(); Console.Error.WriteLine("FAIL: WPF synthetic checks " + actual.GetType().Name + ": " + actual.Message); }
            finally { app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-wpf-check-" + Guid.NewGuid().ToString("N"));
        var secret = EncryptedVault.GenerateRecoverySecret(); MainWindow? main = null;
        try
        {
            main = new MainWindow(root); main.Show(); main.UpdateLayout();
            Invoke(main, "StartSession", EncryptedVault.Create(root, secret, secret));
            var session = Field<SaveCoordinator>(main, "session");
            Control<TextBox>(main, "FolderName").Text = "합성 WPF 폴더";
            Invoke(main, "CreateFolder_Click", main, new RoutedEventArgs());
            Invoke(main, "NewNote_Click", main, new RoutedEventArgs());
            var first = session.Workspace.Notes.Single();
            await Idle(); // Complete the real New-note UI/data-binding turn before text input.
            Require(first.FolderId == session.Workspace.Folders.Single().FolderId, "new-note selected folder assignment failed");
            Require(ReferenceEquals(Control<TextBox>(main, "TitleEditor").DataContext, first) && BindingOperations.IsDataBound(Control<TextBox>(main, "TitleEditor"), TextBox.TextProperty), "title editor binding/context was not active");
            EditText(Control<TextBox>(main, "TitleEditor"), "합성 WPF 제목");
            EditText(Control<TextBox>(main, "BodyEditor"), "본문 전용 합성 WPF");
            await Idle();
            Require(first.Title == "합성 WPF 제목", "title text-container edit did not update shared draft");
            Require(first.Text == "본문 전용 합성 WPF", "body text-container edit did not update shared draft");
            Require(BindingOperations.IsDataBound(Control<TextBox>(main, "BodyEditor"), TextBox.TextProperty), "editing removed body binding");
            Require(Control<TextBlock>(main,"NoteInfo").Text.Contains("본문 12글자"), "Korean body character count failed");
            EditText(Control<TextBox>(main,"BodyEditor"), "가👩‍💻e\u0301"); await Idle();
            Require(Control<TextBlock>(main,"NoteInfo").Text.Contains("본문 3글자"), "grapheme count must not split emoji/combining character");
            EditText(Control<TextBox>(main,"BodyEditor"),"본문 전용 합성 WPF"); await Idle();
            Control<TextBox>(main, "SearchInput").Text = "본문 전용";
            Control<ComboBox>(main, "SearchFieldFilter").SelectedIndex = 1;
            Require(Control<ListBox>(main, "NotesList").Items.Count == 0, "title-only search matched body");
            Control<ComboBox>(main, "SearchFieldFilter").SelectedIndex = 2;
            Require(Control<ListBox>(main, "NotesList").Items.Count == 1, "body-only search failed");
            Control<TextBox>(main, "SearchInput").Clear();
            Control<TextBox>(main, "TagsInput").Text = "합성태그"; Invoke(main, "ApplyTags_Click", main, new RoutedEventArgs());
            Require(first.Metadata.TagIds.Length == 1, "tag control apply failed");
            Require(await session.SaveAsync(), "initial UI snapshot save failed");
            first.Text = "현재 합성 버전"; Require(await session.SaveAsync(), "UI history save failed");
            Invoke(main, "OpenSticky_Click", main, new RoutedEventArgs());
            var sticky = Field<Dictionary<Guid, StickyNoteWindow>>(main, "stickyWindows")[first.Id];
            await Idle(); EditText(Control<TextBox>(sticky, "BodyEditor"), "공유 포스트잇 수정"); await Idle();
            Require(first.Text == "공유 포스트잇 수정" && Control<TextBox>(main, "BodyEditor").Text == first.Text, "sticky/management shared binding failed");
            Invoke(main,"NewNote_Click", main, new RoutedEventArgs()); await Idle();
            var other = session.Workspace.Notes.Single(n=>n.Id!=first.Id); EditText(Control<TextBox>(main,"TitleEditor"),"합성 순서 메모"); await Idle();
            Control<ComboBox>(main,"SortFilter").SelectedIndex=3; Invoke(main,"OrderUp_Click",main,new RoutedEventArgs()); await Idle();
            Require(ReferenceEquals(Control<ListBox>(main,"NotesList").Items[0],other), "UI custom order up action failed");
            session.Workspace.DeleteNote(other); Invoke(main,"RefreshNotes",first); await Idle();
            var history = new HistoryWindow(first, session.Workspace.HistoryFor(first), _ => { }) { Owner = main }; history.Show();
            Field<HashSet<HistoryWindow>>(main, "historyWindows").Add(history);
            Require(Control<TextBox>(history, "PastText").Text.Length > 0, "history preview failed");
            Control<TextBox>(main, "TagFilter").Text = "합성태그";
            await session.LockAsync(); await Idle();
            Require(first.IsClosed && first.Text == "" && session.KeysReleased, "Core lock revocation failed");
            Require(Control<FrameworkElement>(main, "EditingPanel").Visibility == Visibility.Collapsed && Control<ListBox>(main, "NotesList").Items.Count == 0, "lock left results visible/bound");
            Require(Control<TextBox>(main, "SearchInput").Text == "" && Control<TextBox>(main, "TagFilter").Text == "" && Control<TextBox>(main, "TagsInput").Text == "", "lock left query/tag data");
            Require(new[] { "SearchInput", "TagFilter", "FolderName", "TagsInput" }.All(name => !Control<TextBox>(main, name).CanUndo), "lock left search/organization undo text");
            Require(Control<TextBox>(main, "BodyEditor").Text == "" && !Control<TextBox>(main, "BodyEditor").CanUndo && !Control<TextBox>(main, "TitleEditor").CanUndo, "lock left text/undo plaintext");
            Require(Control<ComboBox>(main, "FolderFilter").Items.Count == 0 && Control<TextBlock>(main, "Counts").Text == "", "lock left organization/counts");
            Require(!sticky.IsVisible && sticky.DataContext is null && Control<TextBox>(sticky, "BodyEditor").Text == "" && !Control<TextBox>(sticky, "BodyEditor").CanUndo, "lock left sticky plaintext/undo");
            Require(!history.IsVisible && Control<TextBox>(history, "PastText").Text == "" && Control<ListBox>(history, "Revisions").Items.Count == 0, "lock left history plaintext");
            Invoke(main, "ReleaseSettledSession");
            Invoke(main, "StartSession", EncryptedVault.Open(root, secret));
            var reopened = Field<SaveCoordinator>(main, "session");
            Require(reopened.Workspace.Notes.Single(n=>!n.IsDeleted).Text == "공유 포스트잇 수정", "WPF lock/reopen persisted latest");
            var reopenedNote = reopened.Workspace.Notes.Single(n=>!n.IsDeleted); reopened.Workspace.DeleteNote(reopenedNote);
            Control<ComboBox>(main, "ViewFilter").SelectedIndex = 4; await Idle();
            Require(Control<ListBox>(main, "NotesList").Items.Count == 2 && !Control<FrameworkElement>(main, "Editor").IsEnabled && Control<Button>(main, "RestoreButton").IsEnabled, "trash selection must show readonly content and restore action");
            reopened.Workspace.RestoreNote(reopenedNote); Control<ComboBox>(main, "ViewFilter").SelectedIndex = 0; await Idle();
            Require(Control<FrameworkElement>(main, "Editor").IsEnabled, "restored editor not usable");
            await reopened.LockAsync(); Invoke(main, "ReleaseSettledSession");
            SetField(main, "confirmedExit", true); main.Close(); main = null;
        }
        finally
        {
            if (main is not null) { SetField(main, "confirmedExit", true); main.Close(); var s = Field<SaveCoordinator?>(main, "session"); if (s is not null && !s.IsBusy) s.Dispose(); }
            CryptographicOperations.ZeroMemory(secret); if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void EditText(TextBox box, string text) { box.SelectAll(); box.SelectedText = text; }
    private static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static T Control<T>(Window window, string name) where T : class => (window.FindName(name) as T) ?? throw new Exception("Missing WPF control " + name);
    private static T Field<T>(object target, string name) => (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target))!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

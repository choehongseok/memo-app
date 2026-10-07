using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Documents;
using MemoApp.Core.Documents;
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
            try { await RichViewsRun();await Run(); await BatchFailureRun();await DeviceWindowsRun();Console.WriteLine("PASS: actual Windows WPF rich/shared editing plus bound editing/search/organization/batch/comparison/lock clearing and native device-layout/preferences/widget/open-intent regression (not IME/physical mixed-DPI/OS SessionLock/user usability)"); result = 0; }
            catch (Exception e) { var actual = e.GetBaseException(); Console.Error.WriteLine("FAIL: WPF synthetic checks " + actual.GetType().Name + ": " + actual.Message); }
            finally { app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static async Task RichViewsRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="공유 합성 rich";workspace.ConvertMode(note,"rich",true);bool active=true;
        using var first=new StructuredNoteEditor(workspace,note,()=>active,_=>{});using var second=new StructuredNoteEditor(workspace,note,()=>active,_=>{});
        var left=new Window{Content=first,Width=500,Height=400};var right=new Window{Content=second,Width=500,Height=400};left.Show();right.Show();await Idle();
        Require(new TextRange(first.RichInput.Document.ContentStart,first.RichInput.Document.ContentEnd).Text.Contains("공유 합성 rich"),"canonical rich source must project to real WPF document");
        first.RichInput.Selection.Select(first.RichInput.Document.ContentStart,first.RichInput.Document.ContentEnd);first.ApplyBold();await Idle();
        Require(note.Document!.SourceJson.Contains("\"bold\":true") && new TextRange(second.RichInput.Document.ContentStart,second.RichInput.Document.ContentEnd).Text.Contains(note.Text),"formatting-only source changes propagate between independent WPF documents");
        first.ApplyFontSize(22);await Idle();Require(note.Document!.SourceJson.Contains("22"),"font size formatting stores source value");
        workspace.AcceptPrepared(workspace.Capture());string before=JsonSerializer.Serialize(workspace.Capture());second.ApplyPreferences(new(true,18,1.2));await Idle();Require(JsonSerializer.Serialize(workspace.Capture())==before,"theme/font/view preferences must not rewrite run source or create content revisions");
        var unsafePaste=new DataObject();unsafePaste.SetData(DataFormats.Xaml,"<Paragraph xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Run>UNSAFE</Run></Paragraph>");unsafePaste.SetData(DataFormats.Rtf,"{\\rtf1 UNSAFE}");first.PasteData(unsafePaste);Require(JsonSerializer.Serialize(workspace.Capture())==before,"default rich clipboard formats must be refused without canonical/view mutation");
        var safePaste=new DataObject();safePaste.SetData(DataFormats.UnicodeText,"한글👩‍💻e\u0301");first.PasteData(safePaste);await Idle();Require(note.Text.Contains("한글👩‍💻e\u0301"),"bounded UnicodeText paste updates canonical source");
        active=false;first.ClearSensitive();second.ClearSensitive();workspace.Clear();await Idle();
        Require(first.RichInput.Document.Blocks.Count==0 && second.RichInput.Document.Blocks.Count==0 && !first.RichInput.CanUndo && !second.RichInput.CanUndo && note.Document is null,"conceal/clear cannot save empty projection and must remove document/source/Undo");
        left.Close();right.Close();
    }
    private static async Task BatchFailureRun()
    {
        foreach(bool byteBudget in new[]{false,true})
        {
            var root=Path.Combine(Path.GetTempPath(),"memo-wpf-batch-refusal-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
            try
            {
                var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var snapshot=new VaultSnapshot(3,Guid.NewGuid(),[new(id,Guid.NewGuid(),[],now,now,"synthetic full A",byteBudget?new string('x',3000):"a"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"synthetic B","b")]);
                var history=Enumerable.Range(0,byteBudget?255:512).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"h",byteBudget?new string('x',65536):"history")).ToArray();snapshot=snapshot with{History=history};
                if(byteBudget)
                {
                    int length=JsonSerializer.SerializeToUtf8Bytes(snapshot,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}).Length;
                    int last=65536-(length-(16*1024*1024-232-2400));Require(last is >=0 and <=65536,"WPF whole byte-boundary fixture");history[^1]=history[^1] with{Text=new string('x',last)};
                }
                using(var vault=EncryptedVault.Create(root,secret,secret))vault.Save(snapshot);
                main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));var session=Field<SaveCoordinator>(main,"session");var first=session.Workspace.Notes[0];Invoke(main,"OpenSticky",first);await Idle();Require(await session.SaveAsync(),"WPF batch refusal UI baseline save");await Idle();
                var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[first.Id];var list=Control<ListBox>(main,"NotesList");list.SelectAll();string before=JsonSerializer.Serialize(session.Workspace.Capture());var request=Invoke(main,"CaptureBatch",false)!;
                await (Task)Invoke(main,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
                Require(session.Workspace.Notes.All(n=>!n.IsDeleted) && JsonSerializer.Serialize(session.Workspace.Capture())==before && list.SelectedItems.Count==2 && sticky.IsVisible && sticky.Placement!.State.Open,"failed batch history/byte preflight preserves notes/UI records/selection/open sticky");
                Require(Control<TextBlock>(main,"Notice").Text.Contains("적용하지"),"preflight refusal must report unapplied batch");await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
            }
            finally
            {
                if(main is not null){SetField(main,"confirmedExit",true);main.Close();var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsBusy)active.Dispose();}
                CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
        var failingRoot=Path.Combine(Path.GetTempPath(),"memo-wpf-batch-flush-"+Guid.NewGuid().ToString("N"));var failingSecret=EncryptedVault.GenerateRecoverySecret();MainWindow? failedMain=null;
        try
        {
            var now=DateTimeOffset.UtcNow;using(var vault=EncryptedVault.Create(failingRoot,failingSecret,failingSecret))vault.Save(new(3,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"fault A","a"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"fault B","b")]));
            var original=File.ReadAllBytes(Path.Combine(failingRoot,"current.vault"));failedMain=new MainWindow(failingRoot);failedMain.Show();Invoke(failedMain,"StartSession",EncryptedVault.Open(failingRoot,failingSecret,files:new BatchFaultFiles()));var session=Field<SaveCoordinator>(failedMain,"session");Control<ListBox>(failedMain,"NotesList").SelectAll();var request=Invoke(failedMain,"CaptureBatch",false)!;
            await (Task)Invoke(failedMain,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
            Require(session.Workspace.Notes.All(n=>n.IsDeleted) && session.IsDirty && Control<TextBlock>(failedMain,"Notice").Text.Contains("변경은 반영") && Control<TextBlock>(failedMain,"Notice").Text.Contains("암호 저장 실패") && File.ReadAllBytes(Path.Combine(failingRoot,"current.vault")).SequenceEqual(original),"post-apply disk failure must preserve original and report applied unsaved batch truthfully");
            await session.LockAsync();session.Dispose();SetField(failedMain,"session",(object)null!);SetField(failedMain,"confirmedExit",true);failedMain.Close();failedMain=null;
        }
        finally
        {
            if(failedMain is not null){SetField(failedMain,"confirmedExit",true);failedMain.Close();var active=Field<SaveCoordinator?>(failedMain,"session");if(active is not null&&!active.IsBusy)active.Dispose();}
            CryptographicOperations.ZeroMemory(failingSecret);if(Directory.Exists(failingRoot))Directory.Delete(failingRoot,true);
        }
    }
    private sealed class BatchFaultFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real=new();public Stream CreateNew(string path)=>real.CreateNew(path);
        public void FlushToDisk(Stream stream)=>throw new IOException("Synthetic WPF batch flush fault");
        public void Replace(string temporary,string current,string previous)=>real.Replace(temporary,current,previous);public void Move(string temporary,string current)=>real.Move(temporary,current);
    }
    private static async Task DeviceWindowsRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-device-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();var profile=Guid.NewGuid();MainWindow? main=null;
        try
        {
            main=new MainWindow(root,profile);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");
            var note=session.Workspace.CreateNote();note.Title="합성 장치 창";note.Text="DEVICE_WPF_SYNTHETIC";Require(await session.SaveAsync(),"device WPF initial save");Invoke(main,"RefreshNotes",note);
            Invoke(main,"OpenSticky",note);Invoke(main,"Calendar_Click",main,new RoutedEventArgs());Invoke(main,"Clock_Click",main,new RoutedEventArgs());
            var oldSticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var stale=oldSticky.Placement!;
            Require(session.Workspace.GetUiDevice(profile).Windows.Length==3 && session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"same-turn new memo/widget opening intention must precede native Loaded await");
            await session.LockAsync();await Idle();await stale.RestoreAsync();
            Require(!oldSticky.IsVisible && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==0,"immediate lock must close native memo/date widgets");
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"real encrypted reopen proves security conceal preserved opening intention before Loaded");
            note=session.Workspace.Notes.Single();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var widgets=Field<Dictionary<string,DateWidgetWindow>>(main,"widgets");
            Require(widgets.Count==2 && sticky.IsVisible,"current-profile unlock must reopen saved memo/calendar/clock");await sticky.Placement!.RestoreAsync();await Idle();
            Require(sticky.Placement.State.Monitor!="DEFAULT" && sticky.Placement.State.Dpi>0,"native monitor/DPI layout must be captured");WithinWorkArea(sticky);
            var content=session.Workspace.Capture().Notes.Single();sticky.Left+=35;sticky.Top+=20;sticky.Width=430;sticky.Height=450;await Idle();
            Require(Math.Abs(sticky.Placement.State.Width-430)<2 && Math.Abs(sticky.Placement.State.Height-450)<2,"native move/resize stores DIP dimensions");
            Control<CheckBox>(main,"DarkToggle").IsChecked=true;Invoke(main,"UiPreference_Changed",main,new RoutedEventArgs());Control<Slider>(main,"FontSlider").Value=18;Control<Slider>(main,"ScaleSlider").Value=1.2;await Idle();
            Require(sticky.FontSize==18 && main.FontSize==18 && Math.Abs(Control<ScaleTransform>(sticky,"ContentScale").ScaleX-1.2)<0.001 && Field<TextBlock>(widgets["clock"],"time").FontSize==36,"global font/view scale must affect management/memo/clock");
            var history=new HistoryWindow(note,[],_=>{});Field<HashSet<HistoryWindow>>(main,"historyWindows").Add(history);history.Show();Invoke(main,"ApplyUiPreferences");
            Require(history.FontSize==18 && Control<ScaleTransform>(history,"HistoryScale").ScaleX==1.2 && Control<TextBox>(history,"PastText").Foreground==Brushes.White,"global preferences must reach independent history view");
            double unfolded=sticky.Placement.State.Height;Control<CheckBox>(sticky,"FoldToggle").IsChecked=true;await Idle();
            Require(sticky.Placement.State.Folded && sticky.Placement.State.Height==unfolded && Control<TextBox>(sticky,"BodyEditor").Visibility==Visibility.Collapsed,"fold preserves unfolded dimensions");
            Control<CheckBox>(sticky,"FoldToggle").IsChecked=false;Control<CheckBox>(sticky,"PositionToggle").IsChecked=true;sticky.Topmost=true;sticky.Opacity=0.7;await Idle();
            Require(sticky.ResizeMode==ResizeMode.CanMinimize && sticky.Placement.State.PositionLocked && sticky.Placement.State.Topmost && Math.Abs(sticky.Placement.State.Opacity-0.7)<0.001,"position/topmost/opacity state is independent and persisted");
            Require(Field<HwndSource?>(sticky.Placement,"source") is not null && PositionCommandHandled(sticky,0xF010) && PositionCommandHandled(sticky,0xF000) && PositionCommandHandled(sticky,0xF030),"attached native hook must block SC_MOVE/SC_SIZE/SC_MAXIMIZE when position locked");
            Control<CheckBox>(sticky,"PositionToggle").IsChecked=false;Require(!PositionCommandHandled(sticky,0xF010) && !PositionCommandHandled(sticky,0xF000),"unlocked native hook must leave ordinary move/size commands available");Control<CheckBox>(sticky,"PositionToggle").IsChecked=true;
            sticky.Width=2200;sticky.Height=1800;Invoke(main,"ArrangeSticky_Click",main,new RoutedEventArgs());await Idle();WithinWorkArea(sticky);
            Require(sticky.Placement.State.Width<=2000 && sticky.Placement.State.Height<=1600,"arrange must size actual HWND to work-area/schema bounds");
            Invoke(main,"ToggleSticky_Click",main,new RoutedEventArgs());Require(!sticky.IsVisible,"hide all memo native windows");Invoke(main,"ToggleSticky_Click",main,new RoutedEventArgs());Require(sticky.IsVisible,"show all memo native windows");
            Require(session.Workspace.Capture().Notes.Single()==content && session.Workspace.Capture().History.Length==0,"window/theme callbacks must not create content revisions or timestamp noise");
            stale=sticky.Placement;sticky.Close();widgets["clock"].Close();await Idle();
            Require(session.Workspace.GetUiDevice(profile).Windows.Where(w=>w.Kind is "memo" or "clock").All(w=>!w.Open),"user close must record closed independently of conceal");
            Invoke(main,"OpenSticky",note);Invoke(main,"Clock_Click",main,new RoutedEventArgs());
            Require(session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"same-turn closed-to-open intention precedes Loaded");await session.LockAsync();await Idle();
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==1 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==2,"repeat unlock must restore once without duplicate native windows");
            var before=session.Workspace.GetUiDevice(profile);await stale.RestoreAsync();await Idle();Require(session.Workspace.GetUiDevice(profile)==before,"disposed prior-session controller cannot mutate reopened session");
            Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id].Close();Field<Dictionary<string,DateWidgetWindow>>(main,"widgets")["clock"].Close();
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Keys.SequenceEqual(new[]{"calendar"}),"explicit user-close must remain closed on subsequent unlock");
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");
            SetField(main,"confirmedExit",true);main.Close();main=null;
            main=new MainWindow(root,Guid.NewGuid());main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==0 && main.FontSize==14,"another device profile must not inherit native windows/preferences");
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
        }
        finally
        {
            if(main is not null){SetField(main,"confirmedExit",true);main.Close();var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsBusy)session.Dispose();}
            CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static bool PositionCommandHandled(StickyNoteWindow window,int command)
    {
        object[] arguments={new WindowInteropHelper(window).Handle,0x112,(IntPtr)command,IntPtr.Zero,false};
        window.Placement!.GetType().GetMethod("Hook",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window.Placement,arguments);return (bool)arguments[4];
    }
    private static void WithinWorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;Require(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle),(IntPtr)(-4)),"actual HWND must use production PerMonitorV2 manifest");Require(GetWindowRect(handle,out var rect),"native test window rectangle");var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};Require(GetMonitorInfo(MonitorFromWindow(handle,2),ref info),"native test monitor work area");
        Require(rect.Left>=info.Work.Left && rect.Top>=info.Work.Top && rect.Right<=info.Work.Right+1 && rect.Bottom<=info.Work.Bottom+1,"native restore/arrange must fit the runner work area");
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,int flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]private static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
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
            Require(Control<ListBox>(main,"NotesList").SelectionMode==SelectionMode.Extended,"batch list must allow explicit Extended selection");
            Require(await session.SaveAsync(),"batch WPF starting basis save");
            var list=Control<ListBox>(main,"NotesList");Control<ComboBox>(main,"FolderFilter").SelectedIndex=0;list.SelectedItems.Clear();list.SelectedItems.Add(other);list.SelectedItems.Add(first);await Idle();
            Require(Control<FrameworkElement>(main,"Editor").DataContext is null && !Control<FrameworkElement>(main,"Editor").IsEnabled && !Control<Button>(main,"DuplicateButton").IsEnabled,"multiple selection must revoke singleton editor/binding/actions");
            int countBefore=session.Workspace.Notes.Count;var singleBefore=session.Workspace.Capture();
            foreach(string handler in new[]{"Duplicate_Click","ApplyTags_Click","OrderUp_Click","OpenSticky_Click","History_Click"})Invoke(main,handler,main,new RoutedEventArgs());
            Control<ComboBox>(main,"MoveFolder").SelectedIndex=0;Control<ComboBox>(main,"ColorPicker").SelectedIndex=2;await Idle();
            Require(session.Workspace.Notes.Count==countBefore && session.Workspace.Capture().Notes.SequenceEqual(singleBefore.Notes) && Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==1 && Field<HashSet<HistoryWindow>>(main,"historyWindows").Count==0,"direct singleton handlers must reject multiple selection without notes/windows/history changes");
            var batchFolder=session.Workspace.CreateFolder("합성 batch 목표");Invoke(main,"RefreshFolders");await Idle();var batchTarget=Control<ComboBox>(main,"BatchFolder");batchTarget.SelectedItem=batchTarget.Items.Cast<object>().Single(item=>(Guid?)item.GetType().GetProperty("Id")!.GetValue(item)==batchFolder.FolderId);
            Invoke(main,"BatchMove_Click",main,new RoutedEventArgs());await WaitUntil(()=>!session.IsBusy);await Idle();
            Require(first.FolderId==batchFolder.FolderId && other.FolderId==batchFolder.FolderId && list.SelectedItems.Count==2,"actual batch move control preserves surviving selection and moves all selected drafts");
            var request=Invoke(main,"CaptureBatch",false)!;await (Task)Invoke(main,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
            Require(first.IsDeleted && other.IsDeleted && !sticky.IsVisible,"batch delete must close sticky only after successful Deleted publication");
            Control<ComboBox>(main,"ViewFilter").SelectedIndex=4;list.SelectAll();request=Invoke(main,"CaptureBatch",true)!;await (Task)Invoke(main,"ApplyBatch",request,"restore",(object)null!)!;
            Control<ComboBox>(main,"ViewFilter").SelectedIndex=0;list.SelectedItems.Clear();list.SelectedItem=other;await Idle();
            Control<ComboBox>(main,"SortFilter").SelectedIndex=3; Invoke(main,"OrderUp_Click",main,new RoutedEventArgs()); await Idle();
            Require(ReferenceEquals(Control<ListBox>(main,"NotesList").Items[0],other), "UI custom order up action failed");
            session.Workspace.DeleteNote(other); Invoke(main,"RefreshNotes",first); await Idle();
            Invoke(main,"History_Click",main,new RoutedEventArgs());await WaitUntil(()=>Field<HashSet<HistoryWindow>>(main,"historyWindows").Count==1);var history=Field<HashSet<HistoryWindow>>(main,"historyWindows").Single();
            Require(Control<TextBox>(history, "PastText").Text.Length > 0, "history preview failed");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count>1 && Control<ComboBox>(history,"RightRevision").Items.Count>1 && Control<TextBox>(history,"DiffText").Text.Length>0,"history comparison must include current head and dated immutable revisions");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count==session.Workspace.HistoryFor(first).Count+1 && (bool)Control<ComboBox>(history,"RightRevision").SelectedItem.GetType().GetProperty("Current")!.GetValue(Control<ComboBox>(history,"RightRevision").SelectedItem)! ,"comparison includes exact current head and every distinct history revision even when date labels repeat");
            string frozenRight=Control<TextBox>(history,"RightText").Text;first.Text="AFTER_HISTORY_WINDOW_SYNTHETIC";await Idle();Require(Control<TextBox>(history,"RightText").Text==frozenRight,"later draft edit cannot replace captured comparison head");first.Text="공유 포스트잇 수정";
            Control<TextBox>(main, "TagFilter").Text = "합성태그";
            var staleBatch=Invoke(main,"CaptureBatch",false)!;
            await session.LockAsync(); await Idle();
            Require(first.IsClosed && first.Text == "" && session.KeysReleased, "Core lock revocation failed");
            Require(Control<FrameworkElement>(main, "EditingPanel").Visibility == Visibility.Collapsed && Control<ListBox>(main, "NotesList").Items.Count == 0, "lock left results visible/bound");
            Require(Control<TextBox>(main, "SearchInput").Text == "" && Control<TextBox>(main, "TagFilter").Text == "" && Control<TextBox>(main, "TagsInput").Text == "", "lock left query/tag data");
            Require(new[] { "SearchInput", "TagFilter", "FolderName", "TagsInput" }.All(name => !Control<TextBox>(main, name).CanUndo), "lock left search/organization undo text");
            Require(Control<TextBox>(main, "BodyEditor").Text == "" && !Control<TextBox>(main, "BodyEditor").CanUndo && !Control<TextBox>(main, "TitleEditor").CanUndo, "lock left text/undo plaintext");
            Require(Control<ComboBox>(main, "FolderFilter").Items.Count == 0 && Control<TextBlock>(main, "Counts").Text == "", "lock left organization/counts");
            Require(!sticky.IsVisible && sticky.DataContext is null && Control<TextBox>(sticky, "BodyEditor").Text == "" && !Control<TextBox>(sticky, "BodyEditor").CanUndo, "lock left sticky plaintext/undo");
            Require(!history.IsVisible && Control<TextBox>(history, "PastText").Text == "" && Control<ListBox>(history, "Revisions").Items.Count == 0, "lock left history plaintext");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count==0 && Control<ComboBox>(history,"RightRevision").Items.Count==0 && new[]{"LeftText","RightText","LeftTitle","RightTitle","DiffText"}.All(name=>Control<TextBox>(history,name).Text==""&&!Control<TextBox>(history,name).CanUndo) && Control<TextBlock>(history,"ComparisonInfo").Text=="","lock must clear both comparison sources/previews/diff/metadata/Undo");
            Require(Field<object?>(history,"comparisonSources") is null,"lock must release cached comparison source references");
            Invoke(main, "ReleaseSettledSession");
            Invoke(main, "StartSession", EncryptedVault.Open(root, secret));
            var reopened = Field<SaveCoordinator>(main, "session");
            await (Task)Invoke(main,"ApplyBatch",staleBatch,"delete",(object)null!)!;
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
    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);while(!ready()){if(DateTime.UtcNow>deadline)throw new Exception("WPF synthetic wait timed out");await Task.Delay(10);}await Idle();
    }
    private static T Control<T>(Window window, string name) where T : class => (window.FindName(name) as T) ?? throw new Exception("Missing WPF control " + name);
    private static T Field<T>(object target, string name) => (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target))!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

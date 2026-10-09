using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
using MemoApp.Core.Transfer;
using System.Windows.Media;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow : Window
{
    private readonly string root;
    private readonly Guid uiDeviceId;
    private readonly Dictionary<string,DateWidgetWindow> widgets=[];
    private readonly Dictionary<Window,DesktopWindowController> placements=[];
    private bool concealing,hiddenSticky;
    private readonly HashSet<HistoryWindow> historyWindows = [];
    private bool loadingUi = true;
    private CancellationTokenSource fileOperations = new();
    private Point dragStart;
    private Guid? draggingNote, dragCandidate;
    private long dragCandidateEpoch;
    private long dragEpoch;
    private sealed record FolderChoice(Guid? Id, string Name);
    private readonly Dictionary<Guid, StickyNoteWindow> stickyWindows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private SaveCoordinator? session;
    private byte[]? generatedSecret;
    private DateTimeOffset activity = DateTimeOffset.UtcNow;
    private bool closing, confirmedExit, windowClosed, transitionBusy;
    private long uiEpoch;
    private MarkdownNotePreview? markdownPreview;
    private NoteDraft? markdownNote;
    private StructuredNoteEditor? structuredEditor;
    private NoteDraft? structuredNote;
    private AttachmentPanel? attachmentPanel;
    private NoteDraft? attachmentNote;
    private string? selectedBodyMode;
    private NoteDraft? SingleNote=>NotesList.SelectedItems.Count==1?NotesList.SelectedItem as NoteDraft:null;
    private sealed record BatchRequest(SaveCoordinator Session,long Epoch,NoteDraft[] Notes,bool Deleted);
    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoApp", "SyntheticTrial")) { }
    public MainWindow(string dataRoot) : this(dataRoot,Guid.NewGuid()) { }
    public MainWindow(string dataRoot,Guid uiDeviceId)
    {
        if(uiDeviceId==Guid.Empty)throw new ArgumentException("Empty UI profile");this.uiDeviceId=uiDeviceId;
        root = Path.GetFullPath(dataRoot);
        InitializeComponent(); loadingUi = false;
        timer.Tick += Timer_Tick; timer.Start();
        InputManager.Current.PreProcessInput += Activity;
        SystemEvents.SessionSwitch += SessionSwitch;
        Closing += Window_Closing;
        CompositionTarget.Rendering+=SearchPreviewRendering;
        Closed += (_, _) => { windowClosed=true;DisableTray();ClearBackupPreviews();ClearAutomaticBackupViews();ClearAutomaticTrashViews();ClearSearchStateViews();CompositionTarget.Rendering-=SearchPreviewRendering;ClearSearchResultPreview(); fileOperations.Cancel(); fileOperations.Dispose(); timer.Stop(); InputManager.Current.PreProcessInput -= Activity; SystemEvents.SessionSwitch -= SessionSwitch; ClearSecretControls(); };
    }
    private void Activity(object sender, PreProcessInputEventArgs e) => activity = DateTimeOffset.UtcNow;
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            Dispatcher.BeginInvoke(new Action(async () => { ConcealViews(); if (session is not null) await session.LockAsync(); }));
    }
    private async void Timer_Tick(object? sender, EventArgs e)
    {
        var active = session;
        if (active is null || active.IsLocked || active.IsBusy || closing) return;
        if (DateTimeOffset.UtcNow - activity > TimeSpan.FromMinutes(5)) { await active.LockAsync(); return; }
        bool saved=active.IsDirty&&await active.SaveAsync();
        if(ReferenceEquals(session,active)&&!active.IsLocked)await RunAutomaticBackupAsync(saved);
        if(ReferenceEquals(session,active)&&!active.IsLocked)await RunAutomaticTrashAsync(DateTimeOffset.UtcNow);
    }
    private void StartSession(EncryptedVault vault)
    {
        var active = new SaveCoordinator(vault, TimeProvider.System);
        session = active;
        active.Conceal += ConcealViews;
        active.Changed += () => { if (ReferenceEquals(session, active)) {ClearTrayMenu();UpdateStatus();} };
        ShowEditing();
    }
    private void ShowEditing()
    {
        ClearSecretControls();
        if (fileOperations.IsCancellationRequested) { fileOperations.Dispose(); fileOperations = new(); }
        foreach (var input in new[] { SearchInput, TagFilter, FolderName, TagsInput }) input.IsUndoEnabled = true;
        LockPanel.Visibility = Visibility.Collapsed; EditingPanel.Visibility = Visibility.Visible;
        var active = session!;
        active.Workspace.Changed += () =>
        {
            ClearBackupPreviews();
            long epoch = uiEpoch;
            Dispatcher.BeginInvoke(new Action(() => { if (epoch == uiEpoch && ReferenceEquals(session, active) && !active.IsLocked) RefreshNotes(); }));
        };
        RefreshFolders(); RefreshNotes();ApplyUiPreferences();RestoreDeviceWindows();
        activity = DateTimeOffset.UtcNow; UpdateStatus();
    }
    private void ConcealViews()
    {
        uiEpoch++;concealing=true;ClearTrayMenu();ClearBackupPreviews();ClearAutomaticBackupViews();ClearAutomaticTrashViews(); ClearSearchStateViews();ClearSearchResultPreview();fileOperations.Cancel(); draggingNote = dragCandidate = null;concealing=true;selectedBodyMode=null;
        foreach(var widget in widgets.Values.ToArray()){widget.Hide();widget.Close();}
        // Native hiding happens before encryption, async I/O, or clearing bound objects.
        foreach (var window in stickyWindows.Values.ToArray()) { window.Hide(); window.Close(); }
        foreach (var window in historyWindows.ToArray()) { window.Hide(); window.Close(); }
        EditingPanel.Visibility = Visibility.Collapsed;ClearMarkdownPreview();ClearStructuredEditor();ClearAttachmentPanel();
        Editor.DataContext = null; Editor.IsEnabled = false; NotesList.ItemsSource = null;
        BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = false; BodyEditor.Clear(); TitleEditor.Clear();
        loadingUi = true;
        foreach (var input in new[] { SearchInput, TagFilter, FolderName, TagsInput }) { input.IsUndoEnabled = false; input.Clear(); }
        FolderFilter.ItemsSource = MoveFolder.ItemsSource = BatchFolder.ItemsSource = null; FromDate.SelectedDate = UntilDate.SelectedDate = null;
        ViewFilter.SelectedIndex = SearchFieldFilter.SelectedIndex = SortFilter.SelectedIndex = 0;
        ColorPicker.SelectedIndex = ModeChoice.SelectedIndex = -1; Counts.Text = NoteInfo.Text = ""; loadingUi = false;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = BackupButton.IsEnabled = WholeTransferButton.IsEnabled = BackupPreviewButton.IsEnabled = MarkdownImportButton.IsEnabled = ExcelImportButton.IsEnabled = TxtImportButton.IsEnabled = false;
        TxtExportButton.IsEnabled = PdfExportButton.IsEnabled = ExcelExportButton.IsEnabled = WordExportButton.IsEnabled = false;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = RestoreButton.IsEnabled = HistoryButton.IsEnabled = false;
        BatchMoveButton.IsEnabled=BatchDeleteButton.IsEnabled=BatchRestoreButton.IsEnabled=BatchFolder.IsEnabled=BatchTxtExportButton.IsEnabled=false;
        ClearSecretControls(); LockPanel.Visibility = Visibility.Visible;concealing=false;hiddenSticky=false;
    }
    private void UpdateStatus()
    {
        Notice.Text = session?.Status ?? "잠금 — 복구 비밀로 해제하세요.";
        bool enabled = session is not null && !session.IsLocked;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = BackupButton.IsEnabled = WholeTransferButton.IsEnabled = BackupPreviewButton.IsEnabled = MarkdownImportButton.IsEnabled = ExcelImportButton.IsEnabled = TxtImportButton.IsEnabled = enabled;
        BackupPreviewButton.IsEnabled=enabled&&!backupPreviewBusy;UpdateSelectedActions();
    }
    private bool ReleaseSettledSession()
    {
        if (session is null) return true;
        if (session.IsBusy || !session.IsLocked) { Notice.Text = "먼저 잠금 및 저장 종료를 기다려 주세요."; return false; }
        if (session.PendingKind != "none")
        {
            if (MessageBox.Show("미저장 복구 대기 상태입니다. 암호문 사본 저장/후보 복구를 먼저 권장합니다. 현재 메모리의 미저장 변경을 버리고 계속할까요?", "미저장 변경", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        }
        session.Dispose(); session = null; return true;
    }
    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        byte[]? secret = null;
        try
        {
            secret = EncryptedVault.ParseSecret(RecoveryInput.Password); RecoveryInput.Clear();
            if (session is { IsLocked: true, PendingKind: "plaintext-hidden" }) { session.ResumeHidden(secret); ShowEditing(); return; }
            if (!ReleaseSettledSession()) return;
            StartSession(EncryptedVault.Open(root, secret));
        }
        catch { Notice.Text = "해제 실패 — 비밀·파일·다른 실행 중인 앱을 확인하세요. 원본은 덮어쓰지 않았습니다."; }
        finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
    }
    private void GenerateSecret_Click(object sender, RoutedEventArgs e)
    {
        if (EncryptedVault.CandidateNames(root).Length != 0) { Notice.Text = "기존 파일/복구 후보가 있습니다. 새 자료로 덮어쓰지 않고 해제·복구하세요."; return; }
        ClearSecretControls(); generatedSecret = EncryptedVault.GenerateRecoverySecret();
        NewSecretDisplay.Text = EncryptedVault.EncodeSecret(generatedSecret);
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        byte[]? confirm = null;
        try
        {
            if (generatedSecret is null) throw new InvalidOperationException();
            confirm = EncryptedVault.ParseSecret(ConfirmationInput.Password); ConfirmationInput.Clear();
            if (!ReleaseSettledSession()) return;
            var vault = EncryptedVault.Create(root, generatedSecret, confirm);
            StartSession(vault);
            await session!.SaveAsync();
        }
        catch { Notice.Text = "생성 실패 — 복구 비밀 재입력/기존 파일/쓰기 권한을 확인하세요. 기존 자료는 삭제하지 않았습니다."; }
        finally { if (confirm is not null) CryptographicOperations.ZeroMemory(confirm); }
    }
    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session is not { IsLocked: false } active) return;
            var note = active.Workspace.CreateNote();
            if (FolderFilter.SelectedItem is FolderChoice { Id: Guid folder }) active.Workspace.MoveNote(note, folder);
            ViewFilter.SelectedIndex = 0; SearchInput.Clear(); TagFilter.Clear(); RefreshNotes(note);
        }
        catch { Notice.Text = "새 메모를 만들 수 없습니다. 시험판은 최대 100개입니다."; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (session is {IsLocked:false} active){bool dirty=active.IsDirty;bool saved=await active.SaveAsync();if(ReferenceEquals(session,active)&&!active.IsLocked&&dirty&&saved)await RunAutomaticBackupAsync(true);} }
    private async void Lock_Click(object sender, RoutedEventArgs e) { if (session is not null) await session.LockAsync(); else ClearSecretControls(); }
    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingUi) SelectEditor(); }
    private void ClearMarkdownPreview()
    {var previous=markdownPreview;markdownPreview=null;markdownNote=null;MarkdownHost.Content=null;MarkdownHost.Visibility=Visibility.Collapsed;previous?.Dispose();}
    private void ClearStructuredEditor()
    {
        var previous=structuredEditor;structuredEditor=null;structuredNote=null;StructuredHost.Content=null;StructuredHost.Visibility=Visibility.Collapsed;previous?.Dispose();
    }
    private void ClearAttachmentPanel()
    {
        var previous=attachmentPanel;attachmentPanel=null;attachmentNote=null;previous?.Dispose();
        try{AttachmentHost.Content=null;}catch{}
        try{AttachmentHost.Visibility=Visibility.Collapsed;}catch{}
    }
    private void ConfigureAttachmentPanel(NoteDraft? selected)
    {
        if(selected is not {IsClosed:false,IsDeleted:false}||session is not {IsLocked:false} active||concealing){ClearAttachmentPanel();return;}
        if(ReferenceEquals(attachmentNote,selected)&&attachmentPanel is {IsDisposed:false})return;
        ClearAttachmentPanel();long epoch=uiEpoch;bool attached=false;AttachmentPanel? created=null;
        bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked&&ReferenceEquals(SingleNote,selected)&&ReferenceEquals(Editor.DataContext,selected)&&active.Workspace.Notes.Contains(selected)&&selected is {IsClosed:false,IsDeleted:false}&&(!attached||ReferenceEquals(AttachmentHost.Content,created));
        created=new(active,selected,Current,message=>{if(Current())Notice.Text=message;},uiDeviceId);
        if(Current()){attachmentPanel=created;attachmentNote=selected;AttachmentHost.Content=created;AttachmentHost.Visibility=Visibility.Visible;attached=true;}else created.Dispose();
    }
    private void SelectEditor()
    {
        var selected=SingleNote;bool changed=!ReferenceEquals(Editor.DataContext,selected)||selectedBodyMode!=selected?.Mode;
        if(changed){BodyEditor.IsUndoEnabled=TitleEditor.IsUndoEnabled=false;BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.Clear();}
        Editor.DataContext=selected;Editor.IsEnabled=session is {IsLocked:false}&&!concealing&&selected is {IsDeleted:false};
        TitleEditor.IsUndoEnabled=Editor.IsEnabled;selectedBodyMode=selected?.Mode;
        if(selected is {Mode:"rich",IsDeleted:false}&&session is {IsLocked:false} active&&!concealing)
        {
            BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.IsUndoEnabled=false;BodyEditor.Clear();BodyEditor.IsReadOnly=true;BodyEditor.Visibility=Visibility.Collapsed;StructuredHost.Visibility=Visibility.Visible;
            if(!ReferenceEquals(structuredNote,selected)||structuredEditor is null||structuredEditor.IsDisposed)
            {
                ClearStructuredEditor();long epoch=uiEpoch;bool attached=false;StructuredNoteEditor? created=null;
                bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked&&ReferenceEquals(SingleNote,selected)&&ReferenceEquals(Editor.DataContext,selected)&&selected is {IsClosed:false,IsDeleted:false,Mode:"rich"}&&(!attached||ReferenceEquals(StructuredHost.Content,created));
                created=new(active.Workspace,selected,Current,message=>{if(Current())Notice.Text=message;});
                if(Current()){structuredEditor=created;structuredNote=selected;StructuredHost.Content=created;StructuredHost.Visibility=Visibility.Visible;attached=true;created.ApplyPreferences(active.Workspace.GetUiDevice(uiDeviceId).Preferences);}else created.Dispose();
            }
        }
        else
        {
            ClearStructuredEditor();BodyEditor.Visibility=Visibility.Visible;BodyEditor.IsReadOnly=!Editor.IsEnabled||selected?.Mode=="rich";BodyEditor.IsUndoEnabled=Editor.IsEnabled&&selected?.Mode!="rich";
            if(selected?.Mode=="rich"){BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.Text=selected.Text;}
            else if(!BindingOperations.IsDataBound(BodyEditor,TextBox.TextProperty))BodyEditor.SetBinding(TextBox.TextProperty,new Binding(nameof(NoteDraft.Text)){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        }
        if(selected is {Mode:"markdown",IsDeleted:false}&&session is {IsLocked:false} markdownSession&&!concealing)
        {
            MarkdownHost.Visibility=Visibility.Visible;
            if(!ReferenceEquals(markdownNote,selected)||markdownPreview is null||markdownPreview.IsDisposed)
            {
                ClearMarkdownPreview();long epoch=uiEpoch;bool attached=false;MarkdownNotePreview? created=null;
                bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,markdownSession)&&!markdownSession.IsLocked&&ReferenceEquals(SingleNote,selected)&&ReferenceEquals(Editor.DataContext,selected)&&markdownSession.Workspace.Notes.Contains(selected)&&selected is {IsClosed:false,IsDeleted:false,Mode:"markdown"}&&(!attached||ReferenceEquals(MarkdownHost.Content,created));
                created=new(selected,Current);if(Current()){markdownPreview=created;markdownNote=selected;MarkdownHost.Content=created;MarkdownHost.Visibility=Visibility.Visible;attached=true;created.ApplyPreferences(markdownSession.Workspace.GetUiDevice(uiDeviceId).Preferences);}else created.Dispose();
            }
        }
        else ClearMarkdownPreview();
        ConfigureAttachmentPanel(selected);
        bool loading=loadingUi;loadingUi=true;ModeChoice.SelectedItem=ModeChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>(string)i.Tag==selected?.Mode);loadingUi=loading;
        MoveFolder.IsEnabled=TagsInput.IsEnabled=ColorPicker.IsEnabled=ModeChoice.IsEnabled=Editor.IsEnabled;UpdateSelectedActions();UpdateSelectedDetails();RenderSearchResultPreview();if(changed)QueueRecent(selected);
    }
    private void Mode_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(loadingUi||concealing||session is not {IsLocked:false} active||SingleNote is not {IsDeleted:false} note||ModeChoice.SelectedItem is not ComboBoxItem{Tag:string mode}||mode==note.Mode)return;
        long epoch=uiEpoch,version=note.EditVersion;var source=note.Document;string original=note.Mode;
        if(MessageBox.Show("모드 전환은 원래 문서 전체를 이력에 남깁니다. 서식은 단순 본문으로 바뀔 수 있고, 서식 모드로 바꿀 때 CR/LF는 LF로 정규화됩니다. 미지원 문서의 전체 내용을 단순 본문으로 보존할 수는 없습니다. 전환할까요?","문서 모드 전환",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes){SelectEditor();return;}
        if(concealing||epoch!=uiEpoch||!ReferenceEquals(session,active)||active.IsLocked||!ReferenceEquals(SingleNote,note)||note.IsClosed||note.IsDeleted||note.EditVersion!=version||note.Mode!=original||!ReferenceEquals(note.Document,source)){if(!concealing)SelectEditor();return;}
        try{active.Workspace.ConvertMode(note,mode,true);SelectEditor();}catch{SelectEditor();Notice.Text="모드 전환을 적용하지 않았습니다. 문서·전체 저장용량·이력 한도를 확인하세요.";}
    }
    private void OpenSticky_Click(object sender,RoutedEventArgs e)
    {
        if(session is {IsLocked:false}&&SingleNote is NoteDraft {IsDeleted:false} note)OpenSticky(note);
    }
    private void OpenSticky(NoteDraft note)
    {
        if(session is not {IsLocked:false} active||note.IsClosed||note.IsDeleted)return;
        if(stickyWindows.TryGetValue(note.Id,out var existing)){existing.Show();existing.Activate();return;}
        var window=new StickyNoteWindow(note);long epoch=uiEpoch;
        var state=(active.Workspace.GetUiDevice(uiDeviceId).Windows.FirstOrDefault(w=>w.Kind=="memo"&&w.NoteId==note.Id)??DesktopWindowController.DefaultState(window,"memo",note.Id)) with{Open=true};
        try{active.Workspace.SetWindowLayout(uiDeviceId,state);}catch{window.Close();Notice.Text="창을 열 수 없습니다. 저장 상태/한도를 확인하세요.";return;}
        if(epoch!=uiEpoch||!ReferenceEquals(session,active)||active.IsLocked){window.Close();return;}
        stickyWindows.Add(note.Id,window);
        bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked&&stickyWindows.TryGetValue(note.Id,out var current)&&ReferenceEquals(current,window);
        var placement=new DesktopWindowController(window,"memo",note.Id,state,Current,layout=>PersistLayout(active,epoch,layout));
        placements.Add(window,placement);window.SetPlacement(placement);window.SetEditingContext(active.Workspace,Current,message=>{if(Current())Notice.Text=message;});window.SetAttachmentContext(active,Current,message=>{if(Current())Notice.Text=message;},uiDeviceId);window.ApplyUiPreferences(active.Workspace.GetUiDevice(uiDeviceId).Preferences);
        window.Closed+=(_,_)=>{stickyWindows.Remove(note.Id);placements.Remove(window);};window.Show();
    }
    private void PersistLayout(SaveCoordinator active,long epoch,StoredWindowLayout layout)
    {
        if(concealing||epoch!=uiEpoch||!ReferenceEquals(session,active)||active.IsLocked)return;
        try{active.Workspace.SetWindowLayout(uiDeviceId,layout);}catch{Notice.Text="창 설정 저장 실패 — 기존 암호 자료는 보존했습니다. 저장 상태/한도를 확인하세요.";}
    }
    private void RestoreDeviceWindows()
    {
        if(session is not {IsLocked:false} active)return;
        foreach(var state in active.Workspace.GetUiDevice(uiDeviceId).Windows.Where(w=>w.Open).ToArray())
            if(state.Kind=="memo"&&active.Workspace.Notes.FirstOrDefault(n=>n.Id==state.NoteId&&!n.IsDeleted) is NoteDraft note)OpenSticky(note);
            else if(state.Kind!="memo")OpenWidget(state.Kind);
    }
    private void OpenWidget(string kind)
    {
        if(session is not {IsLocked:false} active)return;
        if(widgets.TryGetValue(kind,out var existing)){existing.Show();existing.Activate();return;}
        var window=new DateWidgetWindow(kind);long epoch=uiEpoch;
        var state=(active.Workspace.GetUiDevice(uiDeviceId).Windows.FirstOrDefault(w=>w.Kind==kind)??DesktopWindowController.DefaultState(window,kind,null)) with{Open=true};
        try{active.Workspace.SetWindowLayout(uiDeviceId,state);}catch{window.Close();Notice.Text="위젯을 열 수 없습니다. 저장 상태/한도를 확인하세요.";return;}
        if(epoch!=uiEpoch||!ReferenceEquals(session,active)||active.IsLocked){window.Close();return;}
        widgets.Add(kind,window);
        bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked&&widgets.TryGetValue(kind,out var current)&&ReferenceEquals(current,window);
        var placement=new DesktopWindowController(window,kind,null,state,Current,layout=>PersistLayout(active,epoch,layout));placements.Add(window,placement);
        window.ApplyUiPreferences(active.Workspace.GetUiDevice(uiDeviceId).Preferences);window.Closed+=(_,_)=>{widgets.Remove(kind);placements.Remove(window);};window.Show();
    }
    private void Calendar_Click(object sender,RoutedEventArgs e)=>OpenWidget("calendar");
    private void Clock_Click(object sender,RoutedEventArgs e)=>OpenWidget("clock");
    private void ToggleSticky_Click(object sender,RoutedEventArgs e)
    {
        if(session is not {IsLocked:false})return;hiddenSticky=!hiddenSticky;
        foreach(var window in stickyWindows.Values)if(hiddenSticky)window.Hide();else window.Show();
    }
    private void ArrangeSticky_Click(object sender,RoutedEventArgs e)
    {
        if(session is not {IsLocked:false})return;
        try{int index=0;foreach(var window in stickyWindows.Values)if(placements.TryGetValue(window,out var p))p.Arrange(index++);}catch{Notice.Text="창 정렬 실패 — 모니터/저장 상태를 확인하세요.";}
    }
    private void UiPreference_Changed(object sender,RoutedEventArgs e)=>SaveUiPreferences();
    private void UiSlider_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)=>SaveUiPreferences();
    private void SaveUiPreferences()
    {
        if(loadingUi||session is not {IsLocked:false} active)return;
        try{active.Workspace.SetUiPreferences(uiDeviceId,new(DarkToggle.IsChecked==true,FontSlider.Value,ScaleSlider.Value));ApplyUiPreferences();}
        catch{ApplyUiPreferences();Notice.Text="표시 설정 저장 실패 — 기존 암호 자료는 보존했습니다.";}
    }
    private void ApplyUiPreferences()
    {
        if(session is not {IsLocked:false} active)return;var prefs=active.Workspace.GetUiDevice(uiDeviceId).Preferences;loadingUi=true;
        DarkToggle.IsChecked=prefs.DarkMode;FontSlider.Value=prefs.FontSize;ScaleSlider.Value=prefs.Scale;FontSize=prefs.FontSize;UiScale.ScaleX=UiScale.ScaleY=prefs.Scale;
        Background=prefs.DarkMode?new SolidColorBrush(Color.FromRgb(28,32,40)):Brushes.White;Foreground=prefs.DarkMode?Brushes.White:Brushes.Black;
        Brush controlBackground=prefs.DarkMode?new SolidColorBrush(Color.FromRgb(42,47,57)):Brushes.White;
        foreach(var type in new[]{typeof(TextBox),typeof(ListBox),typeof(ComboBox),typeof(Button)})
        {
            var style=new Style(type);style.Setters.Add(new Setter(Control.BackgroundProperty,controlBackground));style.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));style.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.SlateGray));Resources[type]=style;
        }
        structuredEditor?.ApplyPreferences(prefs);markdownPreview?.ApplyPreferences(prefs);foreach(var window in stickyWindows.Values)window.ApplyUiPreferences(prefs);foreach(var window in widgets.Values)window.ApplyUiPreferences(prefs);foreach(var window in historyWindows)window.ApplyUiPreferences(prefs);loadingUi=false;
    }
    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        byte[]? secret = null;
        try
        {
            secret = EncryptedVault.ParseSecret(RecoveryInput.Password);
            if (!ReleaseSettledSession()) return;
            Candidates.ItemsSource = EncryptedVault.InspectCandidates(root, secret);
            Notice.Text = "검증된 후보만 표시했습니다. 선택 후 명시적으로 복구하세요. 아직 원본을 변경하지 않았습니다.";
        }
        catch { Notice.Text = "후보 검사 실패 — 비밀·파일·다른 실행 중인 앱을 확인하세요."; }
        finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
    }
    private async void Recover_Click(object sender, RoutedEventArgs e)
    {
        byte[]? secret = null;
        try
        {
            if (Candidates.SelectedItem is not RecoveryCandidate candidate) return;
            secret = EncryptedVault.ParseSecret(RecoveryInput.Password); RecoveryInput.Clear();
            if (!ReleaseSettledSession()) return;
            if (MessageBox.Show($"선택한 인증 후보(메모 {candidate.NoteCount}개)를 새 키 epoch의 snapshot으로 적용할까요? 기존 파일과 다른 후보는 보존합니다.", "복구 적용", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var vault = EncryptedVault.Open(root, secret, candidate.Name);
            long capturedEpoch = uiEpoch; transitionBusy = true;
            try { await Task.Run(() => vault.Save(vault.Loaded)); }
            catch { vault.Dispose(); throw; }
            if (capturedEpoch != uiEpoch) { vault.Dispose(); return; }
            StartSession(vault);
        }
        catch { Notice.Text = "복구 실패 — 후보를 보존하고 쓰기를 중단했습니다. 비밀·파일·쓰기 권한을 확인하세요."; }
        finally { transitionBusy = false; if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
    }
    private void ExportPending_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session is not { IsLocked: true, PendingKind: "ciphertext" }) { Notice.Text = "보류 암호문이 없습니다."; return; }
            var dialog = new SaveFileDialog { Filter = "암호 메모 사본|*.vault", FileName = "memo-pending.vault", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            session.ExportPendingCiphertext(dialog.FileName);
            Notice.Text = "보류 암호문 사본을 새 파일로 저장했습니다. 같은 복구 비밀이 필요합니다. 기존 파일 덮어쓰기는 허용하지 않습니다.";
        }
        catch { Notice.Text = "암호문 사본 저장 실패 — 새로운 파일 이름·쓰기 권한을 확인하세요."; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        byte[]? secret = null;
        try
        {
            secret = EncryptedVault.ParseSecret(RecoveryInput.Password);
            if (!ReleaseSettledSession()) return;
            var dialog = new OpenFileDialog { Filter = "암호 메모 사본|*.vault" };
            if (dialog.ShowDialog(this) != true) return;
            EncryptedVault.ImportEncryptedCopy(root, dialog.FileName, secret);
            Candidates.ItemsSource = EncryptedVault.InspectCandidates(root, secret);
            Notice.Text = "사본 인증 후 새 후보로 보존했습니다. 적용하려면 후보를 선택하고 복구하세요.";
        }
        catch { Notice.Text = "암호문 사본 가져오기 실패 — 비밀·형식·파일 크기·쓰기 권한을 확인하세요."; }
        finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
    }
    private void UpdateSelectedActions()
    {
        bool unlocked = session is { IsLocked: false };
        var note = SingleNote;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = TxtExportButton.IsEnabled = unlocked && note is { IsDeleted: false };
        RestoreButton.IsEnabled = unlocked && note is { IsDeleted: true };
        HistoryButton.IsEnabled = unlocked && note is not null;
        var selected=NotesList.SelectedItems.Cast<NoteDraft>().ToArray();bool any=unlocked&&selected.Length>0;
        BatchMoveButton.IsEnabled=BatchDeleteButton.IsEnabled=any&&selected.All(n=>!n.IsDeleted);
        BatchTxtExportButton.IsEnabled=any&&!batchTextBusy&&selected.All(n=>!n.IsClosed&&!n.IsDeleted);
        PdfExportButton.IsEnabled=any&&!pdfExportBusy&&selected.All(n=>!n.IsClosed&&!n.IsDeleted);
        ExcelExportButton.IsEnabled=WordExportButton.IsEnabled=any&&!officeExportBusy&&selected.All(n=>!n.IsClosed&&!n.IsDeleted);
        BatchRestoreButton.IsEnabled=any&&selected.All(n=>n.IsDeleted);BatchFolder.IsEnabled=BatchMoveButton.IsEnabled;
    }
    private void RefreshFolders()
    {
        if (session is not { IsLocked: false } active) return;
        Guid? selected = (FolderFilter.SelectedItem as FolderChoice)?.Id;
        string PathName(StoredFolder f)
        {
            var names = new List<string> { f.Name }; var parent = f.ParentId;
            while (parent is Guid id) { var p = active.Workspace.Folders.Single(x => x.FolderId == id); names.Insert(0, p.Name); parent = p.ParentId; }
            return string.Join(" / ", names);
        }
        var folders = active.Workspace.Folders.Select(f => new FolderChoice(f.FolderId, PathName(f))).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        loadingUi = true;
        FolderFilter.ItemsSource = new[] { new FolderChoice(null, "모든 폴더") }.Concat(folders).ToArray();
        FolderFilter.SelectedItem = ((FolderChoice[])FolderFilter.ItemsSource).FirstOrDefault(f => f.Id == selected) ?? ((FolderChoice[])FolderFilter.ItemsSource)[0];
        MoveFolder.ItemsSource = new[] { new FolderChoice(null, "미분류") }.Concat(folders).ToArray();
        BatchFolder.ItemsSource=MoveFolder.ItemsSource;BatchFolder.SelectedIndex=0;loadingUi = false;
    }
    private void RefreshNotes(NoteDraft? preferred = null)
    {
        if (session is not { IsLocked: false } active || loadingUi || searchBlocked) return;
        var selected=preferred is null?NotesList.SelectedItems.Cast<NoteDraft>().ToArray():[preferred];
        preferred ??= NotesList.SelectedItem as NoteDraft;
        var options = Options(ReadFilters());
        var results = NoteSearch.Find(active.Workspace, options);
        loadingUi = true;
        NotesList.ItemsSource = results;
        // Selector can retain matching selected objects when ItemsSource is replaced.
        // Rebuild exactly the captured surviving set (or explicit preferred singleton).
        NotesList.SelectedItems.Clear();
        foreach(var note in selected.Where(results.Contains))NotesList.SelectedItems.Add(note);
        if(NotesList.SelectedItems.Count==0)NotesList.SelectedItem = preferred is not null && results.Contains(preferred) ? preferred : results.FirstOrDefault();
        loadingUi = false; SelectEditor();
        int trash = active.Workspace.Notes.Count(n => n.IsDeleted), archive = active.Workspace.Notes.Count(n => !n.IsDeleted && n.Archived);
        Counts.Text = $"전체 {active.Workspace.Notes.Count} · 활성 {active.Workspace.Notes.Count - trash - archive} · 보관 {archive} · 휴지통 {trash} · 결과 {results.Length}";
        UpdateSelectedDetails();RefreshSearchStateViews();
    }
    private void UpdateSelectedDetails()
    {
        if (session is not { IsLocked: false } active || SingleNote is not NoteDraft note)
        {
            loadingUi=true;TagsInput.IsUndoEnabled=false;TagsInput.Clear();MoveFolder.SelectedIndex=ColorPicker.SelectedIndex=-1;loadingUi=false;
            NoteInfo.Text=NotesList.SelectedItems.Count>1?$"{NotesList.SelectedItems.Count}개 선택 — 일괄 명령을 사용하세요":"";return;
        }
        TagsInput.IsUndoEnabled=true;
        loadingUi = true;
        if (MoveFolder.ItemsSource is FolderChoice[] choices) MoveFolder.SelectedItem = choices.FirstOrDefault(f => f.Id == note.FolderId);
        if (!TagsInput.IsKeyboardFocusWithin) TagsInput.Text = string.Join(", ", active.Workspace.Tags.Where(t => note.Metadata.TagIds.Contains(t.TagId)).Select(t => t.Name));
        ColorPicker.SelectedItem = ColorPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == note.Color);
        NoteInfo.Text = $"작성 {note.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC · 수정 {note.ModifiedAt:yyyy-MM-dd HH:mm:ss} UTC · 본문 {new StringInfo(note.Text).LengthInTextElements}글자";
        loadingUi = false;
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!loadingUi) RefreshNotes(); }
    private void SearchText_Changed(object sender, TextChangedEventArgs e) { if (!loadingUi) RefreshNotes(); }
    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session is not { IsLocked: false } active) return;
            var folder = active.Workspace.CreateFolder(FolderName.Text, (FolderFilter.SelectedItem as FolderChoice)?.Id);
            FolderName.Clear(); RefreshFolders(); FolderFilter.SelectedItem = ((FolderChoice[])FolderFilter.ItemsSource).Single(f => f.Id == folder.FolderId); RefreshNotes();
        }
        catch { Notice.Text = "폴더 생성 실패 — 이름 중복/길이/폴더 한도를 확인하세요."; }
    }
    private void MoveFolder_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingUi || session is not { IsLocked: false } active || SingleNote is not NoteDraft { IsDeleted: false } note || MoveFolder.SelectedItem is not FolderChoice folder) return;
        try { active.Workspace.MoveNote(note, folder.Id); } catch { Notice.Text = "폴더 이동 실패 — 현재 자료를 유지했습니다."; }
    }
    private BatchRequest? CaptureBatch(bool deleted)
    {
        if(session is not {IsLocked:false} active)return null;var notes=NotesList.SelectedItems.Cast<NoteDraft>().Take(101).ToArray();
        if(notes.Length is <1 or >100||notes.Any(n=>n.IsClosed||n.IsDeleted!=deleted||!active.Workspace.Notes.Contains(n)))return null;
        return new(active,uiEpoch,notes,deleted);
    }
    private bool CurrentBatch(BatchRequest request)=>SameFileSession(request.Session,request.Epoch)&&request.Notes.All(n=>!n.IsClosed&&n.IsDeleted==request.Deleted&&request.Session.Workspace.Notes.Contains(n));
    private async Task ApplyBatch(BatchRequest request,string operation,Guid? folder)
    {
        if(!CurrentBatch(request))return;var versions=request.Notes.Select(n=>n.EditVersion).ToArray();
        try
        {
            switch(operation){case "move":request.Session.Workspace.MoveNotes(request.Notes,folder);break;case "delete":request.Session.Workspace.DeleteNotes(request.Notes);break;case "restore":request.Session.Workspace.RestoreNotes(request.Notes);break;default:throw new ArgumentException("Unknown batch action");}
            if(!SameFileSession(request.Session,request.Epoch))return;
            RefreshNotes();bool saved=await request.Session.SaveAsync();
            if(SameFileSession(request.Session,request.Epoch))Notice.Text=saved?$"{request.Notes.Length}개 일괄 처리 저장됨" : $"{request.Notes.Length}개 일괄 변경은 반영됐지만 암호 저장 실패 — 변경 유지, 저장 상태를 확인하세요.";
        }
        catch
        {
            if(!SameFileSession(request.Session,request.Epoch))return;
            bool applied=request.Notes.Where((n,i)=>n.EditVersion!=versions[i]).Any();
            Notice.Text=applied?"일괄 변경 적용 후 표시/저장 오류 — 현재 변경과 기존 암호 자료를 유지합니다. 저장 상태를 확인하세요.":"일괄 처리 거절 — 대상/폴더/이력·자료 한도를 확인하세요. 전체 변경을 적용하지 않았습니다.";
        }
    }
    private async void BatchMove_Click(object sender,RoutedEventArgs e)
    {
        if(CaptureBatch(false) is BatchRequest request&&BatchFolder.SelectedItem is FolderChoice folder)await ApplyBatch(request,"move",folder.Id);
    }
    private async void BatchDelete_Click(object sender,RoutedEventArgs e)
    {
        if(CaptureBatch(false) is not BatchRequest request)return;
        if(MessageBox.Show($"선택한 {request.Notes.Length}개 메모를 암호 휴지통으로 이동할까요? 내용/이력을 보존하고 복원할 수 있습니다.","일괄 휴지통",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)await ApplyBatch(request,"delete",null);
    }
    private async void BatchRestore_Click(object sender,RoutedEventArgs e)
    {
        if(CaptureBatch(true) is BatchRequest request)await ApplyBatch(request,"restore",null);
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!loadingUi && session is { IsLocked: false } && SingleNote is NoteDraft { IsDeleted: false } note && ColorPicker.SelectedItem is ComboBoxItem { Tag: string color }) note.Color = color;
    }
    private void ApplyTags_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && SingleNote is NoteDraft note) { active.Workspace.SetTags(note, TagsInput.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); UpdateSelectedDetails(); } }
        catch { Notice.Text = "태그 적용 실패 — 태그당 128자/메모당 16개/전체 100개 한도를 확인하세요."; }
    }
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && SingleNote is NoteDraft note) RefreshNotes(active.Workspace.Duplicate(note)); }
        catch { Notice.Text = "복제 실패 — 휴지통 포함 100개 한도를 확인하세요."; }
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session is not { IsLocked: false } active || SingleNote is not NoteDraft { IsDeleted: false } note) return;
            long epoch=uiEpoch;
            if (MessageBox.Show("이 메모를 암호 휴지통으로 이동할까요? 내용과 이력은 보존하고 복원할 수 있습니다.", "휴지통", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes || !SameFileSession(active, epoch, note)) return;
            active.Workspace.DeleteNote(note); RefreshNotes(); await active.SaveAsync();
        }
        catch { Notice.Text = "휴지통 이동 실패 — 저장/이력 한도와 잠금 상태를 확인하세요."; }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && SingleNote is NoteDraft { IsDeleted: true } note) { active.Workspace.RestoreNote(note); RefreshNotes(); await active.SaveAsync(); } }
        catch { Notice.Text = "메모 복원 실패 — 기존 자료를 유지합니다."; }
    }
    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active || SingleNote is not NoteDraft note) return;
        long epoch = uiEpoch;
        if (!await active.SaveAsync() || epoch != uiEpoch || !ReferenceEquals(session, active) || active.IsLocked || active.IsDirty || !ReferenceEquals(SingleNote,note) || note.IsClosed || !active.Workspace.Notes.Contains(note)) return;
        var capturedHead=active.Workspace.Capture().Notes.Single(n=>n.NoteId==note.Id);
        var window = new HistoryWindow(note, active.Workspace.HistoryFor(note), revision =>
        {
            if (epoch != uiEpoch || !ReferenceEquals(session, active) || active.IsLocked || note.IsDeleted) return;
            active.Workspace.RestoreRevision(note, revision); RefreshNotes(note);
        },capturedHead) { Owner = this };
        window.ApplyUiPreferences(active.Workspace.GetUiDevice(uiDeviceId).Preferences);historyWindows.Add(window); window.Closed += (_, _) => historyWindows.Remove(window); window.Show();
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active) return;
        var dialog = new SaveFileDialog { Filter = "암호 메모 백업|*.vault", FileName = $"memo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.vault", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true || active.IsLocked) return;
        long epoch = uiEpoch; bool success = await active.BackupAsync(dialog.FileName);
        if (epoch == uiEpoch && ReferenceEquals(session, active) && !active.IsLocked) Notice.Text = success ? "최신 암호 백업을 새 파일로 저장했습니다. 같은 복구 비밀이 필요합니다." : "백업 실패/상태 변경 — 기존 파일 덮어쓰기 없이 현재 자료를 보존했습니다. 새 이름·경로·쓰기 권한·저장 상태를 확인하세요.";
    }
    private bool SameFileSession(SaveCoordinator active, long epoch, NoteDraft? note = null) =>
        epoch == uiEpoch && ReferenceEquals(session, active) && !active.IsLocked && (note is null || !note.IsClosed && !note.IsDeleted && active.Workspace.Notes.Contains(note));
    private async void TxtImport_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active) return;
        long epoch = uiEpoch; var token = fileOperations.Token; Guid? folder = (FolderFilter.SelectedItem as FolderChoice)?.Id;
        var dialog = new OpenFileDialog { Filter = "UTF8/BOM UTF16 텍스트|*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !SameFileSession(active, epoch)) return;
        try
        {
            var imported = await Task.Run(() => TextTransfer.Read(dialog.FileName, token), token);
            if (!SameFileSession(active, epoch)) return;
            var note = active.Workspace.ImportText(imported.Title, imported.Text, folder);
            if (!SameFileSession(active,epoch,note)) return;
            ViewFilter.SelectedIndex = 0; SearchInput.Clear(); TagFilter.Clear(); RefreshNotes(note);
            Notice.Text = "TXT 원본을 바꾸지 않고 새 메모로 가져왔습니다. UTF8/BOM UTF16만 지원하며 암호 자동 저장 상태를 확인하세요.";
        }
        catch (OperationCanceledException) { }
        catch { if (SameFileSession(active, epoch)) Notice.Text = "TXT 가져오기 실패 — 로컬 일반파일·인코딩·1MiB/65536자/100개 한도·저장 상태를 확인하세요. 원본과 메모 상태는 보존했습니다."; }
    }
    private async void TxtExport_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active || SingleNote is not NoteDraft { IsDeleted: false } note) return;
        long epoch = uiEpoch; var token = fileOperations.Token;
        if (MessageBox.Show("TXT는 암호화되지 않은 제목·본문 사본입니다. 외부 앱·동기화 폴더·백업에 내용이 남을 수 있고 앱 잠금은 이미 저장한 TXT를 보호하지 못합니다. 평문 파일을 만들까요?", "평문 내보내기", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes || !SameFileSession(active, epoch, note)) return;
        var dialog = new SaveFileDialog { Filter = "UTF8 텍스트|*.txt", FileName = "memo-export.txt", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true || !SameFileSession(active, epoch, note)) return;
        try
        {
            string relative = Path.GetRelativePath(root, Path.GetFullPath(dialog.FileName));
            if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative)) throw new IOException("Plaintext output inside active vault refused");
            using var payload = TextTransfer.Capture(note);
            await Task.Run(() => TextTransfer.WritePrepared(payload, dialog.FileName, token), token);
            if (SameFileSession(active, epoch, note)) Notice.Text = "제목·본문을 새 평문 UTF8 TXT로 저장했습니다. 기존 대상은 덮어쓰지 않았습니다.";
        }
        catch (OperationCanceledException) { }
        catch { if (SameFileSession(active, epoch, note)) Notice.Text = "TXT 내보내기 실패 — 새 로컬 파일 이름·권한·본문 인코딩을 확인하세요. 기존 파일은 덮어쓰지 않았으며 실패한 부분 평문 파일이 남을 수 있습니다."; }
    }
    private void OrderUp_Click(object sender, RoutedEventArgs e) => MoveOrder(-1);
    private void OrderDown_Click(object sender, RoutedEventArgs e) => MoveOrder(1);
    private void MoveOrder(int direction)
    {
        if (session is not { IsLocked: false } active || SingleNote is not NoteDraft { IsDeleted: false } note) return;
        if (SortFilter.SelectedIndex != (int)SearchSort.Custom) { SortFilter.SelectedIndex = (int)SearchSort.Custom; RefreshNotes(note); }
        var list = NotesList.Items.Cast<NoteDraft>().ToList(); int index = list.IndexOf(note), target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count) return;
        try { if (direction < 0) active.Workspace.ReorderBefore(note, list[target]); else active.Workspace.ReorderBefore(list[target], note); RefreshNotes(note); }
        catch { Notice.Text = "순서 변경 실패 — 목록 고정 그룹/이력·저장 한도를 확인하세요. 전체 순서는 보존했습니다."; }
    }
    private static ListBoxItem? NoteItem(DependencyObject? element)
    {
        while (element is not null && element is not ListBoxItem)
            element=element switch {Visual visual=>VisualTreeHelper.GetParent(visual),FrameworkContentElement content=>content.Parent,ContentElement content=>ContentOperations.GetParent(content),_=>null};
        return element as ListBoxItem;
    }
    private void NoteDragStart(object sender, MouseButtonEventArgs e)
    {
        dragStart=e.GetPosition(NotesList); dragCandidate=null;
        if(NotesList.SelectedItems.Count!=1)return;
        if(session is {IsLocked:false} active && SortFilter.SelectedIndex==(int)SearchSort.Custom && NoteItem(e.OriginalSource as DependencyObject)?.DataContext is NoteDraft {IsDeleted:false} note && active.Workspace.Notes.Contains(note)) {dragCandidate=note.Id;dragCandidateEpoch=uiEpoch;}
    }
    private void NoteDragMove(object sender, MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed){dragCandidate=null;return;}
        if (NotesList.SelectedItems.Count!=1 || dragCandidate is not Guid candidate || dragCandidateEpoch!=uiEpoch || session is not { IsLocked: false } active || SortFilter.SelectedIndex != (int)SearchSort.Custom) return;
        var note=active.Workspace.Notes.FirstOrDefault(n=>n.Id==candidate&&!n.IsDeleted); if(note is null)return;
        var now = e.GetPosition(NotesList); if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        draggingNote = note.Id; dragEpoch = uiEpoch;
        try { DragDrop.DoDragDrop(NotesList, new DataObject("MemoApp.InternalNoteId", note.Id.ToString("D")), DragDropEffects.Move); }
        finally { draggingNote = dragCandidate = null; }
    }
    private void NoteDragOver(object sender, DragEventArgs e)
    {
        e.Effects = draggingNote is not null && dragEpoch == uiEpoch && session is { IsLocked: false } && e.Data.GetDataPresent("MemoApp.InternalNoteId", false) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
    }
    private void NoteDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (NotesList.SelectedItems.Count!=1 || draggingNote is not Guid internalId || dragEpoch != uiEpoch || session is not { IsLocked: false } active || !e.Data.GetDataPresent("MemoApp.InternalNoteId", false) || e.Data.GetData("MemoApp.InternalNoteId", false) is not string { Length: 36 } raw || !Guid.TryParseExact(raw,"D",out Guid id) || id != internalId) return;
        if (NoteItem(e.OriginalSource as DependencyObject)?.DataContext is not NoteDraft target) return;
        var source = active.Workspace.Notes.FirstOrDefault(n=>n.Id==id && !n.IsDeleted);
        if (source is null || target.IsDeleted) return;
        try { active.Workspace.ReorderBefore(source,target); RefreshNotes(source); }
        catch { Notice.Text = "순서 변경 실패 — 목록 고정 그룹/이력·저장 한도를 확인하세요. 전체 순서는 보존했습니다."; }
    }
    private void ClearSecretControls()
    {
        if (generatedSecret is not null) CryptographicOperations.ZeroMemory(generatedSecret);
        generatedSecret = null; NewSecretDisplay.Clear(); ConfirmationInput.Clear(); RecoveryInput.Clear();
    }
    private void Exit_Click(object sender,RoutedEventArgs e){fullExitRequested=true;ClearTrayMenu();Close();}
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (confirmedExit) return;
        e.Cancel = true;
        if(!closing&&!transitionBusy&&TryHideToTray())return;
        if (transitionBusy) { Notice.Text = "복구 파일 작업이 끝날 때까지 종료를 보류합니다."; return; }
        if (closing) return; closing = true;ClearTrayMenu();
        try
        {
            if (session is not null)
            {
                if(!await LockForCloseAsync(session))return;
                if (!ReleaseSettledSession()) return;
            }
            confirmedExit = true;
            // A locked/no-session close can complete synchronously inside Closing.
            // WPF forbids recursive Close while that event is still unwinding.
            _ = Dispatcher.BeginInvoke(new Action(()=>{if(!windowClosed&&!Dispatcher.HasShutdownStarted)Close();}));
        }
        finally { closing = false; }
    }
}

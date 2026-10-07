using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow : Window
{
    private readonly string root;
    private readonly HashSet<HistoryWindow> historyWindows = [];
    private bool loadingUi = true;
    private sealed record FolderChoice(Guid? Id, string Name);
    private readonly Dictionary<Guid, StickyNoteWindow> stickyWindows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private SaveCoordinator? session;
    private byte[]? generatedSecret;
    private DateTimeOffset activity = DateTimeOffset.UtcNow;
    private bool closing, confirmedExit, transitionBusy;
    private long uiEpoch;
    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoApp", "SyntheticTrial")) { }
    public MainWindow(string dataRoot)
    {
        root = Path.GetFullPath(dataRoot);
        InitializeComponent(); loadingUi = false;
        timer.Tick += Timer_Tick; timer.Start();
        InputManager.Current.PreProcessInput += Activity;
        SystemEvents.SessionSwitch += SessionSwitch;
        Closing += Window_Closing;
        Closed += (_, _) => { timer.Stop(); InputManager.Current.PreProcessInput -= Activity; SystemEvents.SessionSwitch -= SessionSwitch; ClearSecretControls(); };
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
        if (active.IsDirty) await active.SaveAsync();
    }
    private void StartSession(EncryptedVault vault)
    {
        var active = new SaveCoordinator(vault, TimeProvider.System);
        session = active;
        active.Conceal += ConcealViews;
        active.Changed += () => { if (ReferenceEquals(session, active)) UpdateStatus(); };
        ShowEditing();
    }
    private void ShowEditing()
    {
        ClearSecretControls();
        foreach (var input in new[] { SearchInput, TagFilter, FolderName, TagsInput }) input.IsUndoEnabled = true;
        LockPanel.Visibility = Visibility.Collapsed; EditingPanel.Visibility = Visibility.Visible;
        var active = session!;
        active.Workspace.Changed += () =>
        {
            long epoch = uiEpoch;
            Dispatcher.BeginInvoke(new Action(() => { if (epoch == uiEpoch && ReferenceEquals(session, active) && !active.IsLocked) RefreshNotes(); }));
        };
        RefreshFolders(); RefreshNotes();
        activity = DateTimeOffset.UtcNow; UpdateStatus();
    }
    private void ConcealViews()
    {
        uiEpoch++;
        // Native hiding happens before encryption, async I/O, or clearing bound objects.
        foreach (var window in stickyWindows.Values.ToArray()) { window.Hide(); window.Close(); }
        foreach (var window in historyWindows.ToArray()) { window.Hide(); window.Close(); }
        EditingPanel.Visibility = Visibility.Collapsed;
        Editor.DataContext = null; Editor.IsEnabled = false; NotesList.ItemsSource = null;
        BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = false; BodyEditor.Clear(); TitleEditor.Clear();
        loadingUi = true;
        foreach (var input in new[] { SearchInput, TagFilter, FolderName, TagsInput }) { input.IsUndoEnabled = false; input.Clear(); }
        FolderFilter.ItemsSource = MoveFolder.ItemsSource = null; FromDate.SelectedDate = UntilDate.SelectedDate = null;
        ViewFilter.SelectedIndex = SearchFieldFilter.SelectedIndex = SortFilter.SelectedIndex = 0;
        ColorPicker.SelectedIndex = -1; Counts.Text = NoteInfo.Text = ""; loadingUi = false;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = BackupButton.IsEnabled = false;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = RestoreButton.IsEnabled = HistoryButton.IsEnabled = false;
        ClearSecretControls(); LockPanel.Visibility = Visibility.Visible;
    }
    private void UpdateStatus()
    {
        Notice.Text = session?.Status ?? "잠금 — 복구 비밀로 해제하세요.";
        bool enabled = session is not null && !session.IsLocked;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = BackupButton.IsEnabled = enabled;
        UpdateSelectedActions();
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
    private async void Save_Click(object sender, RoutedEventArgs e) { if (session is not null) await session.SaveAsync(); }
    private async void Lock_Click(object sender, RoutedEventArgs e) { if (session is not null) await session.LockAsync(); else ClearSecretControls(); }
    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!loadingUi) SelectEditor(); }
    private void SelectEditor()
    {
        if (!ReferenceEquals(Editor.DataContext, NotesList.SelectedItem)) BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = false;
        Editor.DataContext = NotesList.SelectedItem;
        Editor.IsEnabled = session is { IsLocked: false } && NotesList.SelectedItem is NoteDraft { IsDeleted: false };
        BodyEditor.IsUndoEnabled = TitleEditor.IsUndoEnabled = Editor.IsEnabled;
        UpdateSelectedActions(); UpdateSelectedDetails();
    }
    private void OpenSticky_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } || NotesList.SelectedItem is not NoteDraft { IsDeleted: false } draft) return;
        if (stickyWindows.TryGetValue(draft.Id, out var existing)) { existing.Activate(); return; }
        var window = new StickyNoteWindow(draft); stickyWindows.Add(draft.Id, window);
        window.Closed += (_, _) => stickyWindows.Remove(draft.Id); window.Show();
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
        var note = NotesList.SelectedItem as NoteDraft;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = unlocked && note is { IsDeleted: false };
        RestoreButton.IsEnabled = unlocked && note is { IsDeleted: true };
        HistoryButton.IsEnabled = unlocked && note is not null;
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
        MoveFolder.ItemsSource = new[] { new FolderChoice(null, "미분류") }.Concat(folders).ToArray(); loadingUi = false;
    }
    private void RefreshNotes(NoteDraft? preferred = null)
    {
        if (session is not { IsLocked: false } active || loadingUi) return;
        preferred ??= NotesList.SelectedItem as NoteDraft;
        var options = new SearchOptions
        {
            Query = SearchInput.Text, Field = (SearchField)Math.Max(0, SearchFieldFilter.SelectedIndex), Sort = (SearchSort)Math.Max(0, SortFilter.SelectedIndex),
            View = ViewFilter.SelectedIndex switch { 3 => SearchView.Archive, 4 => SearchView.Trash, _ => SearchView.Active },
            FavoriteOnly = ViewFilter.SelectedIndex == 1, ImportantOnly = ViewFilter.SelectedIndex == 2, UnfiledOnly = ViewFilter.SelectedIndex == 5,
            FolderId = (FolderFilter.SelectedItem as FolderChoice)?.Id, Tag = TagFilter.Text,
            ModifiedFrom = FromDate.SelectedDate is DateTime from ? new DateTimeOffset(from.Date).ToUniversalTime() : null,
            ModifiedUntil = UntilDate.SelectedDate is DateTime until ? new DateTimeOffset(until.Date.AddDays(1)).ToUniversalTime() : null
        };
        var results = NoteSearch.Find(active.Workspace, options);
        loadingUi = true;
        NotesList.ItemsSource = results; NotesList.SelectedItem = preferred is not null && results.Contains(preferred) ? preferred : results.FirstOrDefault();
        loadingUi = false; SelectEditor();
        int trash = active.Workspace.Notes.Count(n => n.IsDeleted), archive = active.Workspace.Notes.Count(n => !n.IsDeleted && n.Archived);
        Counts.Text = $"전체 {active.Workspace.Notes.Count} · 활성 {active.Workspace.Notes.Count - trash - archive} · 보관 {archive} · 휴지통 {trash} · 결과 {results.Length}";
        UpdateSelectedDetails();
    }
    private void UpdateSelectedDetails()
    {
        if (session is not { IsLocked: false } active || NotesList.SelectedItem is not NoteDraft note) { NoteInfo.Text = ""; return; }
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
        if (loadingUi || session is not { IsLocked: false } active || NotesList.SelectedItem is not NoteDraft { IsDeleted: false } note || MoveFolder.SelectedItem is not FolderChoice folder) return;
        try { active.Workspace.MoveNote(note, folder.Id); } catch { Notice.Text = "폴더 이동 실패 — 현재 자료를 유지했습니다."; }
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!loadingUi && session is { IsLocked: false } && NotesList.SelectedItem is NoteDraft { IsDeleted: false } note && ColorPicker.SelectedItem is ComboBoxItem { Tag: string color }) note.Color = color;
    }
    private void ApplyTags_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && NotesList.SelectedItem is NoteDraft note) { active.Workspace.SetTags(note, TagsInput.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); UpdateSelectedDetails(); } }
        catch { Notice.Text = "태그 적용 실패 — 태그당 128자/메모당 16개/전체 100개 한도를 확인하세요."; }
    }
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && NotesList.SelectedItem is NoteDraft note) RefreshNotes(active.Workspace.Duplicate(note)); }
        catch { Notice.Text = "복제 실패 — 휴지통 포함 100개 한도를 확인하세요."; }
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session is not { IsLocked: false } active || NotesList.SelectedItem is not NoteDraft { IsDeleted: false } note) return;
            if (MessageBox.Show("이 메모를 암호 휴지통으로 이동할까요? 내용과 이력은 보존하고 복원할 수 있습니다.", "휴지통", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes || active.IsLocked) return;
            if (stickyWindows.TryGetValue(note.Id, out var window)) { window.Hide(); window.Close(); }
            active.Workspace.DeleteNote(note); RefreshNotes(); await active.SaveAsync();
        }
        catch { Notice.Text = "휴지통 이동 실패 — 저장/이력 한도와 잠금 상태를 확인하세요."; }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        try { if (session is { IsLocked: false } active && NotesList.SelectedItem is NoteDraft { IsDeleted: true } note) { active.Workspace.RestoreNote(note); RefreshNotes(); await active.SaveAsync(); } }
        catch { Notice.Text = "메모 복원 실패 — 기존 자료를 유지합니다."; }
    }
    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active || NotesList.SelectedItem is not NoteDraft note) return;
        long epoch = uiEpoch;
        if (!await active.SaveAsync() || epoch != uiEpoch || !ReferenceEquals(session, active) || active.IsLocked || active.IsDirty) return;
        var window = new HistoryWindow(note, active.Workspace.HistoryFor(note), revision =>
        {
            if (epoch != uiEpoch || !ReferenceEquals(session, active) || active.IsLocked || note.IsDeleted) return;
            active.Workspace.RestoreRevision(note, revision); RefreshNotes(note);
        }) { Owner = this };
        historyWindows.Add(window); window.Closed += (_, _) => historyWindows.Remove(window); window.Show();
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } active) return;
        var dialog = new SaveFileDialog { Filter = "암호 메모 백업|*.vault", FileName = $"memo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.vault", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true || active.IsLocked) return;
        long epoch = uiEpoch; bool success = await active.BackupAsync(dialog.FileName);
        if (epoch == uiEpoch && ReferenceEquals(session, active) && !active.IsLocked) Notice.Text = success ? "최신 암호 백업을 새 파일로 저장했습니다. 같은 복구 비밀이 필요합니다." : "백업 실패/상태 변경 — 기존 파일 덮어쓰기 없이 현재 자료를 보존했습니다. 새 이름·경로·쓰기 권한·저장 상태를 확인하세요.";
    }
    private void ClearSecretControls()
    {
        if (generatedSecret is not null) CryptographicOperations.ZeroMemory(generatedSecret);
        generatedSecret = null; NewSecretDisplay.Clear(); ConfirmationInput.Clear(); RecoveryInput.Clear();
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (confirmedExit) return;
        e.Cancel = true;
        if (transitionBusy) { Notice.Text = "복구 파일 작업이 끝날 때까지 종료를 보류합니다."; return; }
        if (closing) return; closing = true;
        try
        {
            if (session is not null)
            {
                await session.LockAsync();
                if (!ReleaseSettledSession()) return;
            }
            confirmedExit = true; Close();
        }
        finally { closing = false; }
    }
}

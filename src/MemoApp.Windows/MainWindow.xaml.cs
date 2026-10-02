using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public partial class MainWindow : Window
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoApp", "SyntheticTrial");
    private readonly Dictionary<Guid, StickyNoteWindow> stickyWindows = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private SaveCoordinator? session;
    private byte[]? generatedSecret;
    private DateTimeOffset activity = DateTimeOffset.UtcNow;
    private bool closing, confirmedExit, transitionBusy;
    private long uiEpoch;
    public MainWindow()
    {
        InitializeComponent();
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
        LockPanel.Visibility = Visibility.Collapsed; EditingPanel.Visibility = Visibility.Visible;
        NotesList.ItemsSource = session!.Workspace.Notes;
        NotesList.SelectedItem = session.Workspace.Notes.FirstOrDefault();
        activity = DateTimeOffset.UtcNow; UpdateStatus();
    }
    private void ConcealViews()
    {
        uiEpoch++;
        // Native hiding happens before encryption, async I/O, or clearing bound objects.
        foreach (var window in stickyWindows.Values.ToArray()) { window.Hide(); window.Close(); }
        EditingPanel.Visibility = Visibility.Collapsed;
        Editor.DataContext = null; NotesList.ItemsSource = null;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = false;
        ClearSecretControls(); LockPanel.Visibility = Visibility.Visible;
    }
    private void UpdateStatus()
    {
        Notice.Text = session?.Status ?? "잠금 — 복구 비밀로 해제하세요.";
        bool enabled = session is not null && !session.IsLocked;
        NewButton.IsEnabled = SaveButton.IsEnabled = LockButton.IsEnabled = enabled;
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
        try { if (session is { IsLocked: false }) NotesList.SelectedItem = session.Workspace.CreateNote(); }
        catch { Notice.Text = "새 메모를 만들 수 없습니다. 시험판은 최대 100개입니다."; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (session is not null) await session.SaveAsync(); }
    private async void Lock_Click(object sender, RoutedEventArgs e) { if (session is not null) await session.LockAsync(); else ClearSecretControls(); }
    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Editor.DataContext = NotesList.SelectedItem;
        Editor.IsEnabled = session is { IsLocked: false } && NotesList.SelectedItem is NoteDraft;
    }
    private void OpenSticky_Click(object sender, RoutedEventArgs e)
    {
        if (session is not { IsLocked: false } || NotesList.SelectedItem is not NoteDraft draft) return;
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

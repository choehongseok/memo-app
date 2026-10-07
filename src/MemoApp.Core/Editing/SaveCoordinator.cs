using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// Called on a single UI context. File commits run off-context and are strictly chained.
public sealed class SaveCoordinator : IDisposable
{
    private readonly EncryptedVault vault;
    private readonly TimeProvider clock;
    private bool pendingPlaintext;
    private long generation, savedGeneration, preparedGeneration, sessionEpoch;
    private Task<bool> tail = Task.FromResult(true), backupTask = Task.FromResult(true);
    private Task<bool> rootTask=Task.FromResult(false);
    private PreparedSnapshot? pendingCipher;
    private VaultSnapshot? hiddenPlaintext, hiddenBasis;
    private bool disposed;
    public SaveCoordinator(EncryptedVault vault, TimeProvider clock)
    {
        this.vault = vault; this.clock = clock;
        Workspace = new(clock, vault.Loaded);
        Workspace.Changed += WorkspaceChanged;
        if (vault.NeedsInitialSave || vault.Loaded.SchemaVersion < 4) generation = 1;
        Status = generation == 0 ? "저장됨" : "변경됨";
    }
    public EditingWorkspace Workspace { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsDirty => generation > savedGeneration;
    public bool KeysReleased => vault.KeysReleased;
    public bool IsBusy => !tail.IsCompleted || !backupTask.IsCompleted || !rootTask.IsCompleted;
    public string PendingKind => pendingPlaintext ? "plaintext-hidden" : pendingCipher is not null ? "ciphertext" : "none";
    public string Status { get; private set; }
    public event Action? Conceal;
    public event Action? Changed;
    private void WorkspaceChanged()
    {
        if (!IsLocked) { generation++; Status = "변경됨"; Changed?.Invoke(); }
    }
    public Task<bool> SaveAsync()
    {
        if (disposed || IsLocked || vault.IsFaulted) return Task.FromResult(false);
        if (generation <= preparedGeneration) return tail;
        try
        {
            var snapshot = Workspace.Capture();
            var prepared = vault.Prepare(snapshot);
            Workspace.AcceptPrepared(snapshot);
            preparedGeneration = generation;
            pendingCipher = prepared;
            Status = "저장 중"; Changed?.Invoke();
            tail = WriteAsync(tail, prepared, generation, sessionEpoch);
            return tail;
        }
        catch
        {
            Status = "암호화 준비 실패 — 변경 유지"; Changed?.Invoke(); return Task.FromResult(false);
        }
    }
    internal Task<bool> EnsureAttachmentRootAsync()
    {
        if(disposed||IsLocked||vault.IsFaulted)return Task.FromResult(false);
        if(!rootTask.IsCompleted)return rootTask;
        if(vault.AttachmentRootAnchored)return Task.FromResult(true);
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rootTask=completion.Task;_ = CompleteRootAsync(completion,sessionEpoch);return rootTask;
    }
    private async Task CompleteRootAsync(TaskCompletionSource<bool> completion,long epoch)
    {bool success;try{success=await AnchorRootAsync(epoch);}catch{success=false;}completion.TrySetResult(success);}
    private async Task<bool> AnchorRootAsync(long epoch)
    {
        bool accepted=false;
        try
        {
            var snapshot=vault.InitializeAttachmentRoot(Workspace.Capture());
            var prepared=vault.Prepare(snapshot);Workspace.AcceptPrepared(snapshot);accepted=true;
            generation++;preparedGeneration=generation;pendingCipher=prepared;
            tail=WriteAsync(tail,prepared,generation,epoch);
            Status="첨부 저장 준비 중";Changed?.Invoke();
            if(!await tail)return false;
            return !disposed&&!IsLocked&&epoch==sessionEpoch&&vault.AttachmentRootAnchored;
        }
        catch{if(!accepted)vault.CancelUnpreparedAttachmentRoot();if(!disposed&&!IsLocked&&epoch==sessionEpoch){Status="첨부 저장 준비 실패 — 기존 자료 유지";Changed?.Invoke();}return false;}
    }
    private async Task<bool> WriteAsync(Task<bool> previous, PreparedSnapshot prepared, long capturedGeneration, long capturedEpoch)
    {
        bool success = false;
        if (await previous)
        {
            try { await Task.Run(() => vault.Commit(prepared)); success = true; }
            catch { success = false; }
        }
        if (success && ReferenceEquals(pendingCipher, prepared)) pendingCipher = null;
        if (!disposed && capturedEpoch == sessionEpoch && !IsLocked)
        {
            if (success) savedGeneration = Math.Max(savedGeneration, capturedGeneration);
            Status = !success ? "저장 실패 — 변경 유지, 자동 쓰기 중단" : IsDirty ? "변경됨" : "저장됨";
            Changed?.Invoke();
        }
        return success;
    }
    public async Task<bool> LockAsync()
    {
        if (IsLocked)
        {
            bool settled = await tail; await backupTask; await rootTask;
            return settled && !pendingPlaintext && pendingCipher is null;
        }
        IsLocked = true; sessionEpoch++;
        vault.RevokeAttachmentUse(); // Recovery-held keys do not authorize attachment plaintext/sealing.
        // Conceal all native windows and block input before any save, await, or key-release wait.
        Conceal?.Invoke();
        Status = "잠금 처리 중"; Changed?.Invoke();
        if (generation > preparedGeneration)
        {
            VaultSnapshot? snapshot = null;
            try
            {
                snapshot = Workspace.Capture();
                var prepared = vault.Prepare(snapshot);
                pendingCipher = prepared;
                preparedGeneration = generation;
                snapshot = null;
                tail = WriteAsync(tail, prepared, generation, sessionEpoch);
            }
            catch
            {
                pendingPlaintext = true; hiddenPlaintext = snapshot; hiddenBasis = Workspace.FrozenBasis;
                Status = "잠금 — 암호화 전 실패, 숨겨진 복구 대기(메모리 키 보유)";
            }
        }
        Workspace.Clear();
        if (!pendingPlaintext) vault.ReleaseKeys();
        Changed?.Invoke();
        bool result = await tail;
        await backupTask; // Native conceal/key release above are immediate; close/dispose waits for ciphertext I/O.
        await rootTask;
        Status = pendingPlaintext ? "잠금 — 숨겨진 복구 대기(메모리 키 보유)" :
            pendingCipher is not null ? "잠금 — 미저장 암호문 보류, 키 종료됨" : "잠금 — 키 종료됨";
        Changed?.Invoke();
        return result && !pendingPlaintext && pendingCipher is null;
    }
    public void ResumeHidden(byte[] secret)
    {
        if (!IsLocked || !pendingPlaintext || hiddenPlaintext is null || IsBusy || !vault.VerifyRecoverySecret(secret))
            throw new InvalidOperationException("Hidden recovery requires the correct secret and a settled writer");
        vault.ValidateHiddenRoot(hiddenPlaintext);
        var resumed=new EditingWorkspace(clock,hiddenBasis,hiddenPlaintext);
        try{vault.ResumeAttachmentUse(secret,hiddenPlaintext);}catch{resumed.Clear();throw;}
        Workspace=resumed;Workspace.Changed += WorkspaceChanged;
        hiddenPlaintext = hiddenBasis = null; pendingPlaintext = false; IsLocked = false; sessionEpoch++;
        preparedGeneration = savedGeneration;
        Status = "복구 대기 변경 재개 — 저장되지 않음"; Changed?.Invoke();
    }
    public Task<bool> BackupAsync(string path, IAtomicVaultFiles? backupFiles = null)
    {
        if (disposed || IsLocked || vault.IsFaulted || !backupTask.IsCompleted) return Task.FromResult(false);
        backupTask = BackupCoreAsync(path, backupFiles, sessionEpoch);
        return backupTask;
    }
    private async Task<bool> BackupCoreAsync(string path, IAtomicVaultFiles? backupFiles, long epoch)
    {
        if (!await SaveAsync() || disposed || IsLocked || epoch != sessionEpoch || IsDirty) return false;
        long capturedGeneration = generation;
        try { await Task.Run(() => vault.ExportCommitted(path, backupFiles)); }
        catch { return false; }
        return !disposed && !IsLocked && epoch == sessionEpoch && generation == capturedGeneration && !IsDirty;
    }
    public void ExportPendingCiphertext(string path)
    {
        if (pendingCipher is null) throw new InvalidOperationException("No encrypted pending snapshot");
        var files = new AtomicVaultFiles();
        using var output = files.CreateNew(path); output.Write(pendingCipher.Bytes); files.FlushToDisk(output);
    }
    public void Dispose()
    {
        if (IsBusy) throw new InvalidOperationException("Await writes before disposing the writer");
        disposed = true; IsLocked = true; sessionEpoch++;
        Workspace.Clear(); hiddenPlaintext = hiddenBasis = null; pendingCipher = null;
        vault.Dispose();
    }
}

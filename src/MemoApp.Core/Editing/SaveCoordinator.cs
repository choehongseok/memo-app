using System.Collections.Specialized;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// Called on a single UI context. File commits run off-context and are strictly chained.
public sealed class SaveCoordinator : IDisposable
{
    private readonly EncryptedVault vault;
    private readonly TimeProvider clock;
    private bool pendingPlaintext;
    private long generation, savedGeneration, preparedGeneration, sessionEpoch;
    private Task<bool> tail = Task.FromResult(true);
    private PreparedSnapshot? pendingCipher;
    private VaultSnapshot? hiddenPlaintext;
    private bool disposed;
    public SaveCoordinator(EncryptedVault vault, TimeProvider clock)
    {
        this.vault = vault; this.clock = clock;
        Workspace = new(clock, vault.Loaded);
        foreach (var draft in Workspace.Notes) Observe(draft);
        ((INotifyCollectionChanged)Workspace.Notes).CollectionChanged += CollectionChanged;
        if (vault.NeedsInitialSave) generation = 1;
        Status = generation == 0 ? "저장됨" : "변경됨";
    }
    public EditingWorkspace Workspace { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsDirty => generation > savedGeneration;
    public bool KeysReleased => vault.KeysReleased;
    public bool IsBusy => !tail.IsCompleted;
    public string PendingKind => pendingPlaintext ? "plaintext-hidden" : pendingCipher is not null ? "ciphertext" : "none";
    public string Status { get; private set; }
    public event Action? Conceal;
    public event Action? Changed;
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (NoteDraft draft in e.NewItems) Observe(draft);
        if (!IsLocked) { generation++; Status = "변경됨"; Changed?.Invoke(); }
    }
    private void Observe(NoteDraft draft) => draft.PropertyChanged += (_, e) =>
    {
        if (!IsLocked && e.PropertyName == nameof(NoteDraft.EditVersion)) { generation++; Status = "변경됨"; Changed?.Invoke(); }
    };
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
        if (IsLocked) return await tail && !pendingPlaintext && pendingCipher is null;
        IsLocked = true; sessionEpoch++;
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
                pendingPlaintext = true; hiddenPlaintext = snapshot;
                Status = "잠금 — 암호화 전 실패, 숨겨진 복구 대기(메모리 키 보유)";
            }
        }
        Workspace.Clear();
        if (!pendingPlaintext) vault.ReleaseKeys();
        Changed?.Invoke();
        bool result = await tail;
        Status = pendingPlaintext ? "잠금 — 숨겨진 복구 대기(메모리 키 보유)" :
            pendingCipher is not null ? "잠금 — 미저장 암호문 보류, 키 종료됨" : "잠금 — 키 종료됨";
        Changed?.Invoke();
        return result && !pendingPlaintext && pendingCipher is null;
    }
    public void ResumeHidden(byte[] secret)
    {
        if (!IsLocked || !pendingPlaintext || hiddenPlaintext is null || IsBusy || !vault.VerifyRecoverySecret(secret))
            throw new InvalidOperationException("Hidden recovery requires the correct secret and a settled writer");
        Workspace = new(clock, vault.Loaded, hiddenPlaintext);
        foreach (var draft in Workspace.Notes) Observe(draft);
        ((INotifyCollectionChanged)Workspace.Notes).CollectionChanged += CollectionChanged;
        hiddenPlaintext = null; pendingPlaintext = false; IsLocked = false; sessionEpoch++;
        preparedGeneration = savedGeneration;
        Status = "복구 대기 변경 재개 — 저장되지 않음"; Changed?.Invoke();
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
        Workspace.Clear(); hiddenPlaintext = null; pendingCipher = null;
        vault.Dispose();
    }
}

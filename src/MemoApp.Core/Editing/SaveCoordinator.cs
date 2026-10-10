using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;

// Called on a single UI context. File commits run off-context and are strictly chained.
public sealed partial class SaveCoordinator : IDisposable
{
    private readonly EncryptedVault vault;
    private readonly TimeProvider clock;
    private readonly AttachmentReadTracker attachmentReads = new();
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
        Workspace.AttachmentReadInvalidating += RevokeAttachmentReads;
        if (vault.NeedsInitialSave || vault.Loaded.SchemaVersion < 4) generation = 1;
        Status = generation == 0 ? "저장됨" : "변경됨";
    }
    public EditingWorkspace Workspace { get; private set; }
    public bool IsLocked { get; private set; }
    public Guid VaultIdentity=>vault.Identity;
    public bool IsDirty => generation > savedGeneration;
    public bool KeysReleased => vault.KeysReleased;
    public bool IsBusy => !tail.IsCompleted || !backupTask.IsCompleted || !rootTask.IsCompleted;
    public string PendingKind => pendingPlaintext ? "plaintext-hidden" : pendingCipher is not null ? "ciphertext" : "none";
    public string Status { get; private set; }
    public event Action? Conceal;
    public event Action? Changed;
    public Task WhenAttachmentReadsIdle => attachmentReads.WhenIdle;
    public long AttachmentPreviewEpoch { get; private set; }
    private void RevokeAttachmentReads(NoteDraft? source)
    {
        AttachmentPreviewEpoch++;
        attachmentReads.Revoke(source?.AttachmentReadIdentity);
    }
    public bool IsAttachmentPreviewCurrent(NoteDraft note,Guid id,long expectedVersion,long expectedEpoch)
    {
        if(expectedEpoch!=AttachmentPreviewEpoch)return false;
        try{RequireAttachmentSource(note,expectedVersion,sessionEpoch);_=Workspace.AttachmentObject(note,id);return true;}
        catch{return false;}
    }
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
    private void RequireAttachmentSource(NoteDraft note,long expectedVersion,long epoch)
    {
        if(disposed||IsLocked||epoch!=sessionEpoch||vault.IsFaulted||note.EditVersion!=expectedVersion)throw new InvalidOperationException("Attachment source authority changed");
        Workspace.RequireAttachmentNote(note);
    }
    public NoteDraft[] ImportSelectedEncryptedBackup(byte[] cipher,Guid[] selected,long expectedPreviewEpoch)
    {
        void Current(){if(disposed||IsLocked||vault.IsFaulted||AttachmentPreviewEpoch!=expectedPreviewEpoch)throw new InvalidOperationException("Selected backup authority ended");}
        Current();var backup=vault.AuthenticateBackupSnapshot(cipher);Current();
        return Workspace.ImportBackupNotes(backup,selected,candidate=>{Current();vault.ValidateImportedCandidate(candidate);Current();});
    }
    public EncryptedBackupPreview PreviewEncryptedBackup(byte[] cipher)
    {
        if(disposed||IsLocked||vault.IsFaulted)throw new InvalidOperationException("Backup preview authority ended");
        return vault.PreviewEncryptedBackup(cipher);
    }
    public Task<bool> PrepareAttachmentsAsync()=>EnsureAttachmentRootAsync();
    // Predict only this coordinator's synchronous acceptance. Callers must still reject any additional revocation.
    public (Task<bool> Completion,long PreviewEpoch) PrepareAttachments(long expectedPreviewEpoch)
    {
        if(disposed||IsLocked||vault.IsFaulted||AttachmentPreviewEpoch!=expectedPreviewEpoch)throw new InvalidOperationException("Attachment preparation authority changed");
        long expected=AttachmentPreviewEpoch+(!rootTask.IsCompleted||vault.AttachmentRootAnchored?0:1);
        return (EnsureAttachmentRootAsync(),expected);
    }

    public Guid AttachBytes(NoteDraft note,ReadOnlySpan<byte> bytes,string name,string mime,long expectedVersion)
    {
        long epoch=sessionEpoch;RequireAttachmentSource(note,expectedVersion,epoch);
        var item=vault.EncryptAttachment(bytes,name,mime);
        RequireAttachmentSource(note,expectedVersion,epoch);Workspace.AddAttachment(note,item);return item.ObjectId;
    }
    public void DetachAttachment(NoteDraft note,Guid id,long expectedVersion)
    {
        RequireAttachmentSource(note,expectedVersion,sessionEpoch);Workspace.DetachAttachment(note,id);
    }
    internal byte[] ReadAttachmentBytes(NoteDraft note,Guid id,long expectedVersion)
    {
        long epoch=sessionEpoch;RequireAttachmentSource(note,expectedVersion,epoch);
        var plaintext=vault.DecryptAttachment(Workspace.AttachmentObject(note,id));bool returned=false;
        try{RequireAttachmentSource(note,expectedVersion,epoch);returned=true;return plaintext;}
        finally{if(!returned)System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);}
    }
    public string AttachmentExportRoot(NoteDraft note,Guid id,long expectedVersion,long expectedPreviewEpoch)
    {
        if(!IsAttachmentPreviewCurrent(note,id,expectedVersion,expectedPreviewEpoch))throw new InvalidOperationException("Attachment export authority ended");
        return vault.DataRoot;
    }
    public AttachmentReadLease CreateAttachmentReadLease(NoteDraft note, Guid id, long expectedVersion)
    {
        long epoch = sessionEpoch;
        RequireAttachmentSource(note, expectedVersion, epoch);
        var item = Workspace.AttachmentObject(note, id);
        var slot = attachmentReads.Reserve(note.AttachmentReadIdentity); // No draft/event/key-owner reference escapes.
        byte[]? plaintext = null;
        AttachmentReadLease? lease = null;
        bool returned = false;
        try
        {
            RequireAttachmentSource(note, expectedVersion, epoch);
            if (!ReferenceEquals(item, Workspace.AttachmentObject(note, id))) throw new InvalidOperationException("Attachment object changed");
            plaintext = vault.DecryptAttachment(item);
            RequireAttachmentSource(note, expectedVersion, epoch);
            if (!ReferenceEquals(item, Workspace.AttachmentObject(note, id))) throw new InvalidOperationException("Attachment object changed");
            lease = new AttachmentReadLease(plaintext, slot); plaintext = null;
            if (!slot.Bind(lease)) throw new InvalidOperationException("Pending attachment read was revoked");
            returned = true; return lease;
        }
        finally
        {
            if (!returned)
            {
                if (plaintext is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
                lease?.Dispose(); slot.Finish();
            }
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
        if (!success) RevokeAttachmentReads(null); // Fault revocation precedes public status callbacks.
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
        var settlingBackup=backupTask;
        if (IsLocked)
        {
            bool settled = await tail; await settlingBackup; await rootTask;
            return settled && !pendingPlaintext && pendingCipher is null;
        }
        IsLocked = true; sessionEpoch++;
        RevokeAttachmentReads(null);
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
        await settlingBackup; // Native conceal/key release above are immediate; close/dispose waits for ciphertext I/O.
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
        Workspace.AttachmentReadInvalidating += RevokeAttachmentReads;
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
        try { using var prepared=vault.CaptureCommittedCopy();await WriteDetachedBackupAsync(prepared,path,backupFiles); }
        catch { return false; }
        return !disposed && !IsLocked && epoch == sessionEpoch && generation == capturedGeneration && !IsDirty;
    }
    private static Task WriteDetachedBackupAsync(PreparedEncryptedCopy prepared,string path,IAtomicVaultFiles? files)=>Task.Run(()=>prepared.WriteTo(path,files));
    public Task<bool> LockWithBackupAsync(string path,IAtomicVaultFiles? files=null)
    {
        if(disposed||IsLocked)return Task.FromResult(false);
        // Lock synchronously conceals/releases keys and snapshots the old backup task before this assignment.
        var locking=LockAsync();backupTask=LockedBackupCoreAsync(locking,path,files,sessionEpoch);return backupTask;
    }
    private async Task<bool> LockedBackupCoreAsync(Task<bool> locking,string path,IAtomicVaultFiles? files,long epoch)
    {
        if(!await locking||disposed||!IsLocked||!vault.KeysReleased||epoch!=sessionEpoch)return false;
        try{using var prepared=vault.CaptureLockedCommittedCopy();await WriteDetachedBackupAsync(prepared,path,files);return !disposed&&IsLocked&&epoch==sessionEpoch;}
        catch{return false;}
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
        RevokeAttachmentReads(null);
        try { Workspace.Clear(); }
        finally { hiddenPlaintext = hiddenBasis = null; pendingCipher = null; vault.Dispose(); }
    }
}

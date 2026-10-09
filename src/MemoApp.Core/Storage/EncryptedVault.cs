using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MemoApp.Core.Storage;

public sealed class PreparedSnapshot
{
    internal PreparedSnapshot(byte[] bytes, byte[]? expected, Guid id,Guid attachmentRootId=default) { Bytes = bytes; Expected = expected; Id = id; AttachmentRootId=attachmentRootId; }
    internal byte[] Bytes { get; }
    internal byte[]? Expected { get; }
    internal Guid Id { get; }
    internal Guid AttachmentRootId {get;}
}
public sealed record CandidateState(string Name, string State);
public sealed record RecoveryCandidate(string Name, ulong Sequence, int NoteCount);
public sealed class EncryptedVault : IDisposable
{
    private readonly string root;
    private readonly FileStream writerLock;
    private readonly IAtomicVaultFiles files;
    private readonly Guid vaultId;
    private readonly Guid epoch = Guid.NewGuid();
    private readonly byte[] vaultKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] recoveryKey;
    private byte[]? attachmentRootKey;
    private Guid attachmentRootId;
    private bool attachmentRootAnchored;
    private bool attachmentRootPrepared;
    private bool searchStatePrepared;
    private volatile bool attachmentUseAllowed=true;
    private long attachmentUseEpoch;
    private readonly Dictionary<Guid,StoredAttachmentObject> knownAttachmentObjects=[];
    private readonly object gate = new();
    private readonly object commitGate = new();
    private byte[]? reservedBase;
    private byte[]? lastKnownBase;
    private ulong sequence, wraps;
    private bool disposed, keysReleased;
    private volatile bool faulted;
    public VaultSnapshot Loaded { get; private set; }
    public bool KeysReleased => keysReleased;
    public bool NeedsInitialSave => reservedBase is null;
    internal bool VerifyRecoverySecret(byte[] secret) => !keysReleased && secret.Length == 32 && CryptographicOperations.FixedTimeEquals(secret, recoveryKey);
    public bool IsFaulted => faulted;
    internal bool AttachmentRootAnchored {get{lock(gate)return attachmentRootAnchored&&attachmentUseAllowed&&!keysReleased&&!disposed&&!faulted;}}
    public IReadOnlyList<CandidateState> FailureCandidates { get; private set; } = [];
    private EncryptedVault(string root, FileStream writerLock, byte[] recoveryKey, VaultSnapshot loaded, Guid vaultId, ulong sequence, ulong wraps, byte[]? current, IAtomicVaultFiles files, byte[]? authenticated = null,byte[]? ownedAttachmentRoot=null,bool rootAnchored=false)
    {
        this.root = root; this.writerLock = writerLock; this.files = files;
        this.recoveryKey = (byte[])recoveryKey.Clone(); Loaded = loaded; this.vaultId = vaultId;
        this.sequence = sequence; this.wraps = wraps; reservedBase = current; lastKnownBase = authenticated;
        foreach(var item in loaded.AttachmentObjects)knownAttachmentObjects.Add(item.ObjectId,item);
        attachmentRootId=loaded.AttachmentRootId;attachmentRootKey=ownedAttachmentRoot;attachmentRootAnchored=rootAnchored;
    }
    public static byte[] GenerateRecoverySecret() => RandomNumberGenerator.GetBytes(32);
    public static string EncodeSecret(byte[] secret)
    {
        if (secret.Length != 32) throw new ArgumentException("Recovery secret must be 32 bytes");
        return Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static byte[] ParseSecret(string text)
    {
        if (text.Length != 43 || !Regex.IsMatch(text, "^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid recovery secret");
        var key = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + "=");
        if (key.Length != 32 || EncodeSecret(key) != text) { CryptographicOperations.ZeroMemory(key); throw new ArgumentException("Invalid recovery secret"); }
        return key;
    }
    public static EncryptedVault Create(string root, byte[] secret, byte[] confirmation, IAtomicVaultFiles? files = null)
    {
        if (secret.Length != 32 || confirmation.Length != 32 || !CryptographicOperations.FixedTimeEquals(secret, confirmation)) throw new ArgumentException("Recovery confirmation mismatch");
        root = Path.GetFullPath(root);
        var writer = Acquire(root);
        try
        {
            if (Directory.EnumerateFiles(root, "*.vault").Any()) throw new IOException("Existing vault candidates must be recovered, not overwritten");
            return new(root, writer, secret, new(1, Guid.NewGuid(), []), Guid.NewGuid(), 0, 0, null, files ?? new AtomicVaultFiles());
        }
        catch { writer.Dispose(); throw; }
    }
    public static EncryptedVault Open(string root, byte[] secret, string candidate = "current.vault", IAtomicVaultFiles? files = null)
    {
        if (secret.Length != 32) throw new ArgumentException("Recovery secret must be 32 bytes");
        ValidateName(candidate); root = Path.GetFullPath(root);
        var writer = Acquire(root);
        try
        {
            var expectedCurrent = candidate == "current.vault" ? null : Fingerprint(Path.Combine(root, "current.vault"));
            var candidateBytes = ReadBounded(Path.Combine(root, candidate));
            using var decoded = VaultEnvelope.DecryptOwned(candidateBytes, secret);
            byte[]? transferred=null;
            try
            {
                if(decoded.Snapshot.SchemaVersion is 5 or 6)transferred=decoded.TakeRootKey();
                var fingerprint=SHA256.HashData(candidateBytes);
                var result=new EncryptedVault(root, writer, secret, decoded.Snapshot, decoded.Header.VaultId, decoded.Header.Sequence,
                    decoded.Header.WrapCount, candidate == "current.vault" ? fingerprint : expectedCurrent, files ?? new AtomicVaultFiles(), fingerprint,transferred,candidate=="current.vault"&&(decoded.Snapshot.SchemaVersion is 5 or 6));
                transferred=null;return result;
            }
            finally{if(transferred is not null)CryptographicOperations.ZeroMemory(transferred);}
        }
        catch { writer.Dispose(); throw; }
    }
    public static string[] CandidateNames(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.vault").Where(p => IsName(Path.GetFileName(p))).OrderByDescending(p => Path.GetFileName(p) == "current.vault").ThenByDescending(File.GetLastWriteTimeUtc).Take(128).Select(Path.GetFileName).Cast<string>().ToArray() : [];
    public static RecoveryCandidate[] InspectCandidates(string root, byte[] secret)
    {
        root = Path.GetFullPath(root);
        using var writer = Acquire(root);
        var result = new List<RecoveryCandidate>();
        foreach (var name in CandidateNames(root))
        {
            try
            {
                using var decoded = VaultEnvelope.DecryptOwned(ReadBounded(Path.Combine(root, name)), secret);
                result.Add(new(name, decoded.Header.Sequence, decoded.Snapshot.Notes.Length));
            }
            catch (Exception e) when (e is IOException or CryptographicException or InvalidOperationException or ArgumentException) { }
        }
        return result.OrderByDescending(c => c.Sequence).ToArray();
    }
    public static string ImportEncryptedCopy(string root, string source, byte[] secret)
    {
        var bytes = ReadBounded(source);
        using(var decoded=VaultEnvelope.DecryptOwned(bytes, secret)){ /* Validate all objects, then release the temporary root before I/O. */ }
        root = Path.GetFullPath(root);
        using var writer = Acquire(root);
        string name = $"pending-{Guid.NewGuid():N}.vault";
        var files = new AtomicVaultFiles();
        using var output = files.CreateNew(Path.Combine(root, name)); output.Write(bytes); files.FlushToDisk(output);
        return name;
    }
    public PreparedSnapshot Prepare(VaultSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed || keysReleased || faulted) throw new InvalidOperationException("Vault session cannot prepare writes");
            VaultEnvelope.Validate(snapshot);
            RequireRootSnapshot(snapshot);
            if (snapshot.SchemaVersion < 4) snapshot = snapshot with { SchemaVersion = 4 };
            ulong reservation=snapshot.SchemaVersion is 5 or 6?3UL:2UL;
            if (wraps > VaultEnvelope.MaxWraps - reservation || sequence == ulong.MaxValue) throw new InvalidOperationException("Key use or sequence budget exhausted");
            // Count every attempt, including failures. Rollback of persisted counters cannot be proven.
            wraps += reservation; sequence++;
            var id = Guid.NewGuid();
            var plaintext = SnapshotSerialization.Bytes(snapshot);
            try
            {
                var header=new EnvelopeHeader(vaultId,epoch,id,sequence,wraps,plaintext.Length);
                var bytes = snapshot.SchemaVersion is 5 or 6?AttachmentEnvelope.Encrypt(plaintext,vaultKey,recoveryKey,header,attachmentRootId,attachmentRootKey!):VaultEnvelope.Encrypt(plaintext, vaultKey, recoveryKey,header);
                var prepared = new PreparedSnapshot(bytes, reservedBase, id,snapshot.AttachmentRootId);
                reservedBase = SHA256.HashData(bytes);
                if(snapshot.SchemaVersion is 5 or 6)attachmentRootPrepared=true;
                if(snapshot.SchemaVersion==6)searchStatePrepared=true;
                if(snapshot.SchemaVersion is 5 or 6)foreach(var item in snapshot.AttachmentObjects)knownAttachmentObjects.TryAdd(item.ObjectId,item);
                return prepared;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
    public void Save(VaultSnapshot snapshot) => Commit(Prepare(snapshot));
    public void Commit(PreparedSnapshot prepared)
    {
        lock (commitGate)
        {
            if (disposed || faulted) throw new InvalidOperationException("Vault session cannot commit writes");
            var main = Path.Combine(root, "current.vault");
            var temp = Path.Combine(root, $"pending-{prepared.Id:N}.vault");
            var previous = Path.Combine(root, $"previous-{Guid.NewGuid():N}.vault");
            try
            {
                if (!Equal(Fingerprint(main), prepared.Expected)) throw new IOException("Base snapshot changed; automatic overwrite refused");
                using (var stream = files.CreateNew(temp)) { stream.Write(prepared.Bytes); files.FlushToDisk(stream); }
                // Recheck after I/O; an actor ignoring the process lock can still mutate files.
                if (!Equal(Fingerprint(main), prepared.Expected)) throw new IOException("Base snapshot changed during save");
                if (File.Exists(main)) files.Replace(temp, main, previous); else files.Move(temp, main);
                var actual = Fingerprint(main);
                if (!Equal(actual, SHA256.HashData(prepared.Bytes))) throw new IOException("Commit outcome uncertain");
                lastKnownBase = actual;
                lock(gate)
                    if(!keysReleased&&!disposed&&prepared.AttachmentRootId!=Guid.Empty&&prepared.AttachmentRootId==attachmentRootId&&attachmentRootKey is not null)attachmentRootAnchored=true;
            }
            catch
            {
                faulted = true;
                FailureCandidates = Classify(main, previous, temp, prepared);
                throw new IOException("Save failed; candidates retained and further writes stopped");
            }
        }
    }
    private CandidateState[] Classify(string main, string previous, string temp, PreparedSnapshot prepared)
    {
        var expectedNew = SHA256.HashData(prepared.Bytes);
        return new[] { main, previous, temp }.Select(path =>
        {
            string state;
            try
            {
                var hash = Fingerprint(path);
                state = hash is null ? "missing" : Equal(hash, expectedNew) ? "known-authenticated-new" : Equal(hash, lastKnownBase) ? "known-authenticated-base" : "unverified-preserved";
            }
            catch { state = "unreadable-preserved"; }
            return new CandidateState(Path.GetFileName(path), state);
        }).ToArray();
    }
    public void ExportCommitted(string path,IAtomicVaultFiles? backupFiles=null)
    {
        using var prepared=CaptureCommittedCopy();prepared.WriteTo(path,backupFiles);
    }
    public PreparedEncryptedCopy CaptureCommittedCopy()=>CaptureCommittedCopy(false);
    internal PreparedEncryptedCopy CaptureLockedCommittedCopy()=>CaptureCommittedCopy(true);
    private PreparedEncryptedCopy CaptureCommittedCopy(bool requireReleased)
    {
        lock(commitGate)
        {
            lock(gate)
            {
                if(disposed||faulted||lastKnownBase is null||keysReleased!=requireReleased)throw new InvalidOperationException("No authenticated committed copy authority");
                var bytes=ReadBounded(Path.Combine(root,"current.vault"));
                if(!Equal(lastKnownBase,SHA256.HashData(bytes)))throw new IOException("Committed snapshot changed; backup refused");
                return new(bytes,root);
            }
        }
    }
    private static void RejectLinkedAncestors(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup directory refused");
    }
    public void ReleaseKeys()
    {
        RevokeAttachmentUse();
        lock (gate)
        {
            CryptographicOperations.ZeroMemory(vaultKey); CryptographicOperations.ZeroMemory(recoveryKey);
            if(attachmentRootKey is not null){CryptographicOperations.ZeroMemory(attachmentRootKey);attachmentRootKey=null;}attachmentRootAnchored=false;attachmentUseAllowed=false;
            knownAttachmentObjects.Clear();
            Loaded = new(1, Guid.Empty, []); keysReleased = true;
        }
    }
    internal VaultSnapshot InitializeAttachmentRoot(VaultSnapshot snapshot)
    {
        lock(gate)
        {
            if(disposed||keysReleased||faulted)throw new InvalidOperationException("Vault root initialization authority ended");
            VaultEnvelope.Validate(snapshot);
            if(snapshot.SchemaVersion is 5 or 6){RequireRootSnapshot(snapshot);return snapshot;}
            var id=attachmentRootId==Guid.Empty?Guid.NewGuid():attachmentRootId;
            var candidate=snapshot with{SchemaVersion=5,AttachmentRootId=id};VaultEnvelope.Validate(candidate);
            if(attachmentRootKey is null){attachmentRootKey=RandomNumberGenerator.GetBytes(32);attachmentRootId=id;}
            return candidate;
        }
    }
    private void RequireRootSnapshot(VaultSnapshot snapshot)
    {
        if((Loaded.SchemaVersion==6||searchStatePrepared)&&snapshot.SchemaVersion<6)throw new InvalidOperationException("Search UI state downgrade refused");
        if(snapshot.SchemaVersion<5)
        {
            if(attachmentRootId!=Guid.Empty)
            {
                CancelUnpreparedAttachmentRoot();
                if(attachmentRootId!=Guid.Empty)throw new InvalidOperationException("Attachment root downgrade refused");
            }
            return;
        }
        if(attachmentRootKey is null||snapshot.AttachmentRootId!=attachmentRootId)throw new InvalidOperationException("Attachment root must be owned and match the snapshot");
        foreach(var known in knownAttachmentObjects.Values)
        {
            var candidate=snapshot.AttachmentObjects.FirstOrDefault(item=>item.ObjectId==known.ObjectId);
            if(candidate is null||!AttachmentValidation.SameObject(known,candidate))throw new InvalidOperationException("Immutable retained attachment object changed or disappeared");
        }
        foreach(var item in snapshot.AttachmentObjects)
        {
            var plaintext=AttachmentObjectCodec.Decrypt(item,vaultId,attachmentRootId,attachmentRootKey);
            try{}finally{CryptographicOperations.ZeroMemory(plaintext);}
        }
    }
    internal void ValidateHiddenRoot(VaultSnapshot snapshot)
    {lock(gate){if(disposed||keysReleased)throw new InvalidOperationException("Hidden root authority ended");if(snapshot.SchemaVersion is not(1 or 2 or 3 or 4 or 5 or 6))throw new InvalidOperationException("Unknown hidden schema refused");if(snapshot.SchemaVersion<5&&attachmentRootId!=Guid.Empty&&(attachmentRootAnchored||attachmentRootPrepared||Loaded.SchemaVersion is 5 or 6))throw new InvalidOperationException("Hidden attachment root downgrade refused");var objects=AttachmentValidation.Objects(snapshot);foreach(var note in snapshot.Notes)AttachmentValidation.References(note.AttachmentIds,objects,snapshot.SchemaVersion);foreach(var revision in snapshot.History)AttachmentValidation.References(revision.AttachmentIds,objects,snapshot.SchemaVersion);if(snapshot.SchemaVersion is 5 or 6)RequireRootSnapshot(snapshot);}}
    internal void CancelUnpreparedAttachmentRoot()
    {
        lock(gate)
        {
            if(attachmentRootAnchored||attachmentRootPrepared||Loaded.SchemaVersion is 5 or 6||keysReleased)return;
            if(attachmentRootKey is not null)CryptographicOperations.ZeroMemory(attachmentRootKey);
            attachmentRootKey=null;attachmentRootId=Guid.Empty;
        }
    }
    internal void RevokeAttachmentUse()
    {attachmentUseAllowed=false;Interlocked.Increment(ref attachmentUseEpoch);}
    internal void ResumeAttachmentUse(byte[] secret,VaultSnapshot hidden)
    {
        lock(gate)
        {
            if(disposed||keysReleased||!VerifyRecoverySecret(secret))throw new InvalidOperationException("Attachment resume needs current recovery authority");
            ValidateHiddenRoot(hidden);Interlocked.Increment(ref attachmentUseEpoch);attachmentUseAllowed=true;
        }
    }
    internal StoredAttachmentObject EncryptAttachment(ReadOnlySpan<byte> bytes,string name,string mime)
    {
        lock(gate)
        {
            if(!AttachmentRootAnchored||attachmentRootKey is null)throw new InvalidOperationException("Attachment encryption needs a committed active root");
            long epoch=Volatile.Read(ref attachmentUseEpoch);
            var result=AttachmentObjectCodec.Encrypt(bytes,vaultId,attachmentRootId,attachmentRootKey,name,mime);
            RequireAttachmentUse(epoch);return result;
        }
    }
    internal byte[] DecryptAttachment(StoredAttachmentObject item)
    {
        lock(gate)
        {
            if(!AttachmentRootAnchored||attachmentRootKey is null)throw new InvalidOperationException("Attachment decryption needs a committed active root");
            long epoch=Volatile.Read(ref attachmentUseEpoch);
            var plaintext=AttachmentObjectCodec.Decrypt(item,vaultId,attachmentRootId,attachmentRootKey);bool returned=false;
            try{RequireAttachmentUse(epoch);returned=true;return plaintext;}
            finally{if(!returned)CryptographicOperations.ZeroMemory(plaintext);}
        }
    }
    private void RequireAttachmentUse(long epoch)
    {if(epoch!=Volatile.Read(ref attachmentUseEpoch)||!AttachmentRootAnchored)throw new InvalidOperationException("Attachment use was revoked during crypto");}
    public void Dispose()
    {
        RevokeAttachmentUse();
        lock (gate)
        {
            if (disposed) return;
            ReleaseKeys(); writerLock.Dispose(); disposed = true;
        }
    }
    private static FileStream Acquire(string root)
    {
        if (!OperatingSystem.IsWindows()) Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else Directory.CreateDirectory(root); // Per-user AppData ACL inheritance; cross-account access is unverified.
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked vault directory refused");
        var path = Path.Combine(root, "writer.lock");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked writer lock refused");
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static bool IsName(string name) => name == "current.vault" || Regex.IsMatch(name, "^(previous|pending)-[a-f0-9]{32}\\.vault$", RegexOptions.CultureInvariant);
    private static void ValidateName(string name) { if (!IsName(name)) throw new ArgumentException("Invalid candidate name"); }
    internal static byte[] ReadBounded(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked vault file refused");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > VaultEnvelope.MaxFile || file.Length < 1) throw new InvalidDataException("File size outside limits");
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("File changed while reading");
        return bytes;
    }
    private static byte[]? Fingerprint(string path) => File.Exists(path) ? SHA256.HashData(ReadBounded(path)) : null;
    private static bool Equal(byte[]? a, byte[]? b) => a is null || b is null ? a is null && b is null : CryptographicOperations.FixedTimeEquals(a, b);
}

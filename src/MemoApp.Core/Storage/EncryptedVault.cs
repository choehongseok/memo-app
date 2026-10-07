using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace MemoApp.Core.Storage;

public sealed class PreparedSnapshot
{
    internal PreparedSnapshot(byte[] bytes, byte[]? expected, Guid id) { Bytes = bytes; Expected = expected; Id = id; }
    internal byte[] Bytes { get; }
    internal byte[]? Expected { get; }
    internal Guid Id { get; }
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
    public IReadOnlyList<CandidateState> FailureCandidates { get; private set; } = [];
    private EncryptedVault(string root, FileStream writerLock, byte[] recoveryKey, VaultSnapshot loaded, Guid vaultId, ulong sequence, ulong wraps, byte[]? current, IAtomicVaultFiles files, byte[]? authenticated = null)
    {
        this.root = root; this.writerLock = writerLock; this.files = files;
        this.recoveryKey = (byte[])recoveryKey.Clone(); Loaded = loaded; this.vaultId = vaultId;
        this.sequence = sequence; this.wraps = wraps; reservedBase = current; lastKnownBase = authenticated;
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
            var decoded = VaultEnvelope.Decrypt(candidateBytes, secret);
            return new(root, writer, secret, decoded.Snapshot, decoded.Header.VaultId, decoded.Header.Sequence,
                decoded.Header.WrapCount, candidate == "current.vault" ? SHA256.HashData(candidateBytes) : expectedCurrent, files ?? new AtomicVaultFiles(), SHA256.HashData(candidateBytes));
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
                var decoded = VaultEnvelope.Decrypt(ReadBounded(Path.Combine(root, name)), secret);
                result.Add(new(name, decoded.Header.Sequence, decoded.Snapshot.Notes.Length));
            }
            catch (Exception e) when (e is IOException or CryptographicException or InvalidOperationException or ArgumentException) { }
        }
        return result.OrderByDescending(c => c.Sequence).ToArray();
    }
    public static string ImportEncryptedCopy(string root, string source, byte[] secret)
    {
        var bytes = ReadBounded(source);
        _ = VaultEnvelope.Decrypt(bytes, secret);
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
            if (snapshot.SchemaVersion < 3) snapshot = snapshot with { SchemaVersion = 3 };
            if (wraps > VaultEnvelope.MaxWraps - 2 || sequence == ulong.MaxValue) throw new InvalidOperationException("Key use or sequence budget exhausted");
            // Count every attempt, including failures. Rollback of persisted counters cannot be proven.
            wraps += 2; sequence++;
            var id = Guid.NewGuid();
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot, VaultEnvelope.JsonOptions);
            try
            {
                var bytes = VaultEnvelope.Encrypt(plaintext, vaultKey, recoveryKey, new(vaultId, epoch, id, sequence, wraps, plaintext.Length));
                var prepared = new PreparedSnapshot(bytes, reservedBase, id);
                reservedBase = SHA256.HashData(bytes);
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
    public void ExportCommitted(string path, IAtomicVaultFiles? backupFiles = null)
    {
        path = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, path);
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative)) throw new IOException("Backup destination must be outside the active vault");
        RejectLinkedAncestors(Path.GetDirectoryName(path)!);
        lock (commitGate)
        {
            lock (gate)
                if (disposed || keysReleased || faulted || lastKnownBase is null) throw new InvalidOperationException("No active authenticated committed snapshot");
            var bytes = ReadBounded(Path.Combine(root, "current.vault"));
            if (!Equal(lastKnownBase, SHA256.HashData(bytes))) throw new IOException("Committed snapshot changed; backup refused");
            var outputFiles = backupFiles ?? new AtomicVaultFiles();
            using (var output = outputFiles.CreateNew(path)) { output.Write(bytes); outputFiles.FlushToDisk(output); }
            if (!Equal(SHA256.HashData(ReadBounded(path)), lastKnownBase)) throw new IOException("Backup outcome uncertain");
        }
    }
    private static void RejectLinkedAncestors(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup directory refused");
    }
    public void ReleaseKeys()
    {
        lock (gate)
        {
            CryptographicOperations.ZeroMemory(vaultKey); CryptographicOperations.ZeroMemory(recoveryKey);
            Loaded = new(1, Guid.Empty, []); keysReleased = true;
        }
    }
    public void Dispose()
    {
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

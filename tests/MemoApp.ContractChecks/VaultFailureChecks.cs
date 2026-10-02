using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Storage;

internal static class VaultFailureChecks
{
    private static readonly AtomicVaultFiles Real = new();
    internal static bool TryWorker(string[] args)
    {
        if (args.Length != 3 || args[0] != "--vault-worker") return false;
        byte[] secret = EncryptedVault.ParseSecret(Console.ReadLine() ?? "");
        try
        {
            using var vault = EncryptedVault.Open(args[1], secret, files: new FaultFiles(args[2], true));
            if (args[2] == "writer-probe") return true;
            vault.Save(Snapshot("CHILD_SYNTHETIC"));
            throw new Exception("Crash hook did not fire");
        }
        catch (IOException) when (args[2] == "writer-probe") { Environment.ExitCode = 23; return true; }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    internal static VaultSnapshot Snapshot(string marker)
    {
        var now = DateTimeOffset.UtcNow;
        return new(1, Guid.NewGuid(), [new(Guid.NewGuid(), Guid.NewGuid(), [], now, now, "합성 " + marker, "SYNTHETIC_ONLY " + marker)]);
    }
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-faults-" + Guid.NewGuid().ToString("N"));
        var secret = EncryptedVault.GenerateRecoverySecret();
        Directory.CreateDirectory(root);
        try
        {
            using (var vault = EncryptedVault.Create(root, secret, secret)) vault.Save(Snapshot("ORIGINAL"));
            byte[] original = File.ReadAllBytes(Path.Combine(root, "current.vault"));
            var wrongConfirmation = RandomNumberGenerator.GetBytes(32);
            VaultChecks.ExpectFailure(() => EncryptedVault.Create(Path.Combine(root, "confirmation"), secret, wrongConfirmation).Dispose(), "mismatched re-entry must fail");
            CryptographicOperations.ZeroMemory(wrongConfirmation);
            foreach (var malformed in new[] { "", new string('a', 42), new string('a', 44), new string('!', 43) })
                VaultChecks.ExpectFailure(() => EncryptedVault.ParseSecret(malformed), "invalid recovery encoding accepted");
            foreach (var offset in new[] { 0, 8, 12, 16, 32, 48, 64, 72, 80, 84, 96, 128, 144, 156, 188, 204, 216, original.Length - 1 })
            {
                var mutated = (byte[])original.Clone(); mutated[offset] ^= 1;
                File.WriteAllBytes(Path.Combine(root, "current.vault"), mutated);
                VaultChecks.ExpectFailure(() => EncryptedVault.Open(root, secret).Dispose(), "AEAD/header mutation accepted");
            }
            foreach (int length in new[] { 0, 7, 83, 84, 143, 203, original.Length - 1 })
            {
                File.WriteAllBytes(Path.Combine(root, "current.vault"), original[..length]);
                VaultChecks.ExpectFailure(() => EncryptedVault.Open(root, secret).Dispose(), "truncation accepted");
            }
            File.WriteAllBytes(Path.Combine(root, "current.vault"), [.. original, 0]);
            VaultChecks.ExpectFailure(() => EncryptedVault.Open(root, secret).Dispose(), "trailing byte accepted");
            File.WriteAllBytes(Path.Combine(root, "current.vault"), new byte[VaultEnvelope.MaxFile + 1]);
            VaultChecks.ExpectFailure(() => EncryptedVault.Open(root, secret).Dispose(), "oversize accepted");
            File.WriteAllBytes(Path.Combine(root, "current.vault"), original);
            ParserChecks(secret);
            foreach (var stage in new[] { "create", "write", "pre-flush", "post-flush", "pre-replace", "post-replace" })
            {
                File.WriteAllBytes(Path.Combine(root, "current.vault"), original);
                using (var vault = EncryptedVault.Open(root, secret, files: new FaultFiles(stage)))
                {
                    VaultChecks.ExpectFailure(() => vault.Save(Snapshot("FAILED_SAVE")), "fault injection must fail");
                    VaultChecks.Require(vault.IsFaulted && vault.FailureCandidates.Count == 3, "uncertain outcome must classify and stop writer");
                    VaultChecks.ExpectFailure(() => vault.Save(Snapshot("SECOND_SAVE")), "faulted writer must refuse another write");
                }
                using var valid = EncryptedVault.Open(root, secret);
                VaultChecks.Require(valid.Loaded.Notes.Length == 1, "failure destroyed all readable current data");
            }
            File.WriteAllBytes(Path.Combine(root, "current.vault"), original);
            using (var vault = EncryptedVault.Open(root, secret))
            {
                var prepared = vault.Prepare(Snapshot("NEW"));
                File.WriteAllBytes(Path.Combine(root, "current.vault"), [.. original, 0]);
                VaultChecks.ExpectFailure(() => vault.Commit(prepared), "external base mutation must not be overwritten");
                VaultChecks.Require(File.ReadAllBytes(Path.Combine(root, "current.vault")).Length == original.Length + 1, "unexpected base was overwritten");
            }
            File.WriteAllBytes(Path.Combine(root, "current.vault"), original);
            using (var vault = EncryptedVault.Open(root, secret)) vault.Save(Snapshot("SECOND"));
            string preserved = EncryptedVault.CandidateNames(root).First(n => n.StartsWith("previous-"));
            byte[] previous = File.ReadAllBytes(Path.Combine(root, preserved));
            File.WriteAllBytes(Path.Combine(root, "current.vault"), "corrupted"u8.ToArray());
            using (var recovery = EncryptedVault.Open(root, secret, preserved)) recovery.Save(recovery.Loaded);
            VaultChecks.Require(File.ReadAllBytes(Path.Combine(root, preserved)).SequenceEqual(previous), "recovery overwrote sole normal previous");
            File.Delete(Path.Combine(root, "current.vault"));
            VaultChecks.ExpectFailure(() => EncryptedVault.Create(root, secret, secret).Dispose(), "missing main must not silently replace recovery candidates with empty vault");
            using (var recovery = EncryptedVault.Open(root, secret, preserved)) recovery.Save(recovery.Loaded);
            using (var writer = EncryptedVault.Open(root, secret))
                VaultChecks.Require(Child(root, secret, "writer-probe") == 23, "second PROCESS writer was not refused");
            foreach (var stage in new[] { "pre-flush", "post-flush", "pre-replace", "post-replace" })
            {
                int exit = Child(root, secret, stage);
                VaultChecks.Require(OperatingSystem.IsWindows() ? exit != 0 : exit == 137, "worker did not terminate by process kill");
                using var afterCrash = EncryptedVault.Open(root, secret);
                VaultChecks.Require(afterCrash.Loaded.Notes.Length == 1, "crash left no valid current snapshot");
            }
            foreach (var name in EncryptedVault.CandidateNames(root))
            {
                var bytes = File.ReadAllBytes(Path.Combine(root, name));
                VaultChecks.Require(bytes.AsSpan().IndexOf("SYNTHETIC_ONLY"u8) < 0 && bytes.AsSpan().IndexOf(secret) < 0, "plaintext/key leaked into current/previous/pending");
            }
            Console.WriteLine("PASS: 19 region mutations, 7 truncations, strict parser, write/flush/replace faults, base conflict, preserved recovery");
            Console.WriteLine("PASS: second-process writer rejection, 4 actual process kills, current/previous/pending plaintext/key scans");
        }
        finally { CryptographicOperations.ZeroMemory(secret); Directory.Delete(root, true); }
    }
    private static void ParserChecks(byte[] recovery)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var snapshot = Snapshot("PARSER");
            string valid = System.Text.Json.JsonSerializer.Serialize(snapshot, VaultEnvelope.JsonOptions);
            foreach (var raw in new[] { valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), valid.Replace("\"mode\":\"plain\"", "\"mode\":\"future\""), valid.Replace("\"history\":[]", "\"history\":null"), valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2147483648") })
            {
                var bytes = Encoding.UTF8.GetBytes(raw);
                var header = new EnvelopeHeader(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 2, bytes.Length);
                var cipher = VaultEnvelope.Encrypt(bytes, key, recovery, header);
                VaultChecks.ExpectFailure(() => VaultEnvelope.Decrypt(cipher, recovery), "authenticated malformed JSON accepted");
            }
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Notes = [snapshot.Notes[0], snapshot.Notes[0]] }), "duplicate ID accepted");
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Notes = [snapshot.Notes[0] with { Parents = [Guid.NewGuid()] }] }), "dangling parent accepted");
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Notes = [snapshot.Notes[0] with { Title = new string('a', 257) }] }), "title limit ignored");
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Notes = Enumerable.Repeat(snapshot.Notes[0], 101).ToArray() }), "note count limit ignored");
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Notes = [snapshot.Notes[0] with { Text = new string('a', 65537) }] }), "text limit ignored");
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var owner = snapshot.Notes[0].NoteId;
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with {
                History = [new(owner, a, [b], DateTimeOffset.UtcNow, "", ""), new(owner, b, [a], DateTimeOffset.UtcNow, "", "")]
            }), "revision cycle accepted");
            VaultChecks.ExpectFailure(() => VaultEnvelope.Validate(snapshot with { Tombstones = [new(owner, Guid.NewGuid(), [])] }), "live note/tombstone ID collision accepted");
            var tooDeep = Encoding.UTF8.GetBytes(valid.Replace("\"history\":[]", "\"history\":" + new string('[', 17) + new string(']', 17)));
            var tooDeepCipher = VaultEnvelope.Encrypt(tooDeep, key, recovery, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 2, tooDeep.Length));
            VaultChecks.ExpectFailure(() => VaultEnvelope.Decrypt(tooDeepCipher, recovery), "JSON depth limit ignored");
            var root = Path.Combine(Path.GetTempPath(), "memo-budget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var plain = Encoding.UTF8.GetBytes(valid);
                File.WriteAllBytes(Path.Combine(root, "current.vault"), VaultEnvelope.Encrypt(plain, key, recovery,
                    new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ulong.MaxValue, VaultEnvelope.MaxWraps, plain.Length)));
                using var vault = EncryptedVault.Open(root, recovery);
                VaultChecks.ExpectFailure(() => vault.Prepare(snapshot), "wrap/sequence budget guard ignored");
            }
            finally { Directory.Delete(root, true); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static int Child(string root, byte[] secret, string stage)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--vault-worker"); info.ArgumentList.Add(root); info.ArgumentList.Add(stage);
        using var process = Process.Start(info) ?? throw new Exception("Cannot start crash worker");
        process.StandardInput.WriteLine(EncryptedVault.EncodeSecret(secret)); process.StandardInput.Close();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("Crash worker timed out"); }
        var standard = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        VaultChecks.Require(!standard.Contains(EncryptedVault.EncodeSecret(secret), StringComparison.Ordinal) && !errors.Contains(EncryptedVault.EncodeSecret(secret), StringComparison.Ordinal) &&
            !standard.Contains("SYNTHETIC_ONLY", StringComparison.Ordinal) && !errors.Contains("SYNTHETIC_ONLY", StringComparison.Ordinal), "child stdout/stderr leaked secret or note contents");
        // Never emit child output: no keys or notes in parent logs.
        return process.ExitCode;
    }
    internal sealed class FaultFiles(string stage, bool kill = false) : IAtomicVaultFiles
    {
        private void Hit(string point)
        {
            if (stage != point) return;
            if (kill) { Process.GetCurrentProcess().Kill(); throw new IOException("Process termination failed"); }
            throw new IOException("Synthetic storage failure");
        }
        public Stream CreateNew(string path) { Hit("create"); return new WrappedStream((FileStream)Real.CreateNew(path), () => Hit("write")); }
        public void FlushToDisk(Stream stream) { Hit("pre-flush"); Real.FlushToDisk(((WrappedStream)stream).Inner); Hit("post-flush"); }
        public void Replace(string temporary, string current, string previous) { Hit("pre-replace"); Real.Replace(temporary, current, previous); Hit("post-replace"); }
        public void Move(string temporary, string current) { Hit("pre-move"); Real.Move(temporary, current); Hit("post-move"); }
    }
    private sealed class WrappedStream(FileStream inner, Action beforeWrite) : Stream
    {
        internal FileStream Inner => inner;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { beforeWrite(); inner.Write(buffer, offset, count); }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}

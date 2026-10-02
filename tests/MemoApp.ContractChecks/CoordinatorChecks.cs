using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using System.Security.Cryptography;
internal static class CoordinatorChecks
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-coordinator-" + Guid.NewGuid().ToString("N"));
        var secret = EncryptedVault.GenerateRecoverySecret();
        Directory.CreateDirectory(root);
        try
        {
            using (var initial = EncryptedVault.Create(root, secret, secret)) initial.Save(initial.Loaded);
            var blocking = new BlockingFiles();
            using (var session = new SaveCoordinator(EncryptedVault.Open(root, secret, files: blocking), TimeProvider.System))
            {
                var draft = session.Workspace.CreateNote(); draft.Text = "FIRST_SYNTHETIC";
                var firstSave = session.SaveAsync();
                await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                draft.Text = "LATEST_SYNTHETIC";
                VaultChecks.Require(session.IsDirty, "new edit must stay dirty during older save");
                bool concealed = false;
                session.Conceal += () => concealed = true;
                var locking = session.LockAsync();
                VaultChecks.Require(concealed && session.IsLocked && draft.Text == "" && session.Workspace.Notes.Count == 0,
                    "lock must conceal and revoke editors synchronously BEFORE pending disk save");
                VaultChecks.Require(session.KeysReleased, "encrypted pending state must release keys without waiting for disk");
                blocking.Continue.TrySetResult();
                await firstSave; await locking;
                VaultChecks.Require(session.IsLocked && session.KeysReleased, "late async save must not reopen locked views or keys");
            }
            using (var reopened = EncryptedVault.Open(root, secret))
                VaultChecks.Require(reopened.Loaded.Notes[0].Text == "LATEST_SYNTHETIC" && reopened.Loaded.History.Length == 1, "lock must serialize latest edit after earlier save");
            var generationFiles = new BlockingFiles();
            using (var generation = new SaveCoordinator(EncryptedVault.Open(root, secret, files: generationFiles), TimeProvider.System))
            {
                var draft = generation.Workspace.Notes[0]; draft.Text = "OLDER_GENERATION";
                var olderSave = generation.SaveAsync();
                await generationFiles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                draft.Text = "NEWER_GENERATION";
                generationFiles.Continue.TrySetResult();
                await olderSave;
                VaultChecks.Require(generation.IsDirty && generation.Status == "변경됨", "stale completion cleared newer dirty generation");
                VaultChecks.Require(await generation.SaveAsync(), "newer generation could not be saved");
                await generation.LockAsync();
            }
            using (var failure = new SaveCoordinator(EncryptedVault.Open(root, secret, files: new VaultFailureChecks.FaultFiles("pre-flush")), TimeProvider.System))
            {
                var draft = failure.Workspace.Notes[0]; draft.Text = "FAILURE_HIDDEN_SYNTHETIC";
                await failure.LockAsync();
                var exported = Path.Combine(root, "exported-cipher.vault");
                failure.ExportPendingCiphertext(exported);
                VaultChecks.Require(VaultEnvelope.Decrypt(File.ReadAllBytes(exported), secret).Snapshot.Notes[0].Text == "FAILURE_HIDDEN_SYNTHETIC", "pending encrypted export lost unsaved contents");
                VaultChecks.Require(failure.IsLocked && failure.KeysReleased && failure.PendingKind == "ciphertext" && draft.Text == "",
                    "disk failure while locking must never expose plaintext or retain keys unnecessarily");
            }
            using (var invalid = new SaveCoordinator(EncryptedVault.Open(root, secret), TimeProvider.System))
            {
                var draft = invalid.Workspace.Notes[0]; draft.Title = new string('a', 257);
                await invalid.LockAsync();
                VaultChecks.Require(invalid.IsLocked && !invalid.KeysReleased && invalid.PendingKind == "plaintext-hidden" && draft.Title == "",
                    "pre-encryption failure must stay concealed and truthfully report key-holding recovery state");
                var wrong = RandomNumberGenerator.GetBytes(32);
                VaultChecks.ExpectFailure(() => invalid.ResumeHidden(wrong), "hidden recovery must require correct secret");
                CryptographicOperations.ZeroMemory(wrong);
                invalid.ResumeHidden(secret);
                invalid.Workspace.Notes[0].Title = "합성 복구 제목";
                VaultChecks.Require(await invalid.LockAsync() && invalid.KeysReleased, "correct secret must allow fixing hidden draft then saving and locking");
            }
            Console.WriteLine("PASS: generation/epoch, queued latest save, immediate lock during blocked I/O, late completion, locked write/encryption failures");
        }
        finally { CryptographicOperations.ZeroMemory(secret); Directory.Delete(root, true); }
    }
    private sealed class BlockingFiles : IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path) => real.CreateNew(path);
        public void FlushToDisk(Stream stream)
        {
            Entered.TrySetResult();
            if (!Continue.Task.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Synthetic paused writer timeout");
            real.FlushToDisk(stream);
        }
        public void Replace(string temp, string main, string previous) => real.Replace(temp, main, previous);
        public void Move(string temp, string main) => real.Move(temp, main);
    }
}

using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class BackupChecks
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); var secret = EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var session = new SaveCoordinator(EncryptedVault.Create(source, secret, secret), TimeProvider.System);
            var folder = session.Workspace.CreateFolder("합성 백업 폴더");
            var note = session.Workspace.CreateNote(); note.Text = "합성 백업 내용"; session.Workspace.MoveNote(note, folder.FolderId);
            string backup = Path.Combine(root, "manual.vault");
            VaultChecks.Require(await session.BackupAsync(backup), "manual backup must save latest and export encrypted committed bytes");
            VaultChecks.Require(File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "current.vault"))), "backup must exactly match committed ciphertext");
            var destination = Path.Combine(root, "restored"); string candidate = EncryptedVault.ImportEncryptedCopy(destination, backup, secret);
            using (var restored = EncryptedVault.Open(destination, secret, candidate))
            {
                VaultChecks.Require(restored.Loaded.Notes.Single().Text == note.Text && restored.Loaded.Folders.Single().FolderId == folder.FolderId, "actual backup decrypt in separate root"); restored.Save(restored.Loaded);
            }
            var original = File.ReadAllBytes(backup);
            VaultChecks.Require(!await session.BackupAsync(backup) && File.ReadAllBytes(backup).SequenceEqual(original), "existing backup must never overwrite");
            VaultChecks.Require(!await session.BackupAsync(Path.Combine(source, "pending-" + Guid.NewGuid().ToString("N") + ".vault")), "same vault candidate backup path must reject");
            VaultChecks.Require(!await session.BackupAsync(Path.Combine(root, "failed.vault"), new VaultFailureChecks.FaultFiles("pre-flush")), "backup flush error must report failure");
            VaultChecks.Require(await session.SaveAsync(), "backup export error must not fault source writer");
            note.Text = "最新 합성 백업 내용";
            var blocker = new PauseBackupFiles(); var pending = session.BackupAsync(Path.Combine(root, "concurrent.vault"), blocker);
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var locking = session.LockAsync();
            VaultChecks.Require(session.IsLocked && session.KeysReleased && note.Text == "", "backup I/O must not defer conceal/key release");
            bool completionWaitsForBackup = !locking.IsCompleted;
            blocker.Continue.TrySetResult();
            VaultChecks.Require(!await pending, "lock during backup must not report stale UI success"); await locking;
            VaultChecks.Require(completionWaitsForBackup && !session.IsBusy, "lock completion must settle outstanding backup before safe close/dispose");
            VaultChecks.Require(!await session.BackupAsync(Path.Combine(root, "locked.vault")), "locked session backup must reject");
            Console.WriteLine("PASS: latest encrypted manual backup, exact bytes, actual independent-root recovery, no overwrite, flush failure and immediate-lock race");
        }
        finally { CryptographicOperations.ZeroMemory(secret); Directory.Delete(root, true); }
    }
    private sealed class PauseBackupFiles : IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path) => real.CreateNew(path);
        public void FlushToDisk(Stream stream) { Entered.TrySetResult(); if (!Continue.Task.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Synthetic backup pause timed out"); real.FlushToDisk(stream); }
        public void Replace(string temp, string main, string previous) => throw new InvalidOperationException();
        public void Move(string temp, string main) => throw new InvalidOperationException();
    }
}

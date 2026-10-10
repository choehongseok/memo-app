using System.Security.Cryptography;
using MemoApp.Core.Storage;
internal static class VaultChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-vault-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[]? secret = null;
        try
        {
            secret = EncryptedVault.GenerateRecoverySecret();
            Require(secret.Length == 32, "recovery secret must be exactly 32 bytes");
            var parsed = EncryptedVault.ParseSecret(EncryptedVault.EncodeSecret(secret));
            Require(parsed.SequenceEqual(secret), "recovery secret must roundtrip");
            CryptographicOperations.ZeroMemory(parsed);
            var now = DateTimeOffset.UtcNow;
            var snapshot = new VaultSnapshot(1, Guid.NewGuid(),
                [new(Guid.NewGuid(), Guid.NewGuid(), [], now, now, "합성 민감 제목 ONLY_TEST", "합성 민감 본문 ONLY_TEST")]);
            using (var vault = EncryptedVault.Create(root, secret, secret))
            {
                vault.Save(snapshot);
                ExpectFailure(() => EncryptedVault.Open(root, secret).Dispose(), "second writer must fail");
            }
            using (var restored = EncryptedVault.Open(root, secret))
            {
                Require(restored.Loaded.Notes[0].Text == snapshot.Notes[0].Text, "new instance must restore encrypted notes");
            }
            foreach (var file in Directory.GetFiles(root))
            {
                var bytes = File.ReadAllBytes(file);
                Require(!Contains(bytes, System.Text.Encoding.UTF8.GetBytes(snapshot.Notes[0].Text)), "plaintext body leaked to disk");
                Require(!Contains(bytes, secret), "recovery secret leaked to disk");
            }
            string transferred = Path.Combine(root, "transferred");
            string imported = EncryptedVault.ImportEncryptedCopy(transferred, Path.Combine(root, "current.vault"), secret);
            var candidates = EncryptedVault.InspectCandidates(transferred, secret);
            Require(candidates.Length == 1 && candidates[0].Name == imported, "authenticated imported copy was not listed");
            using (var recovered = EncryptedVault.Open(transferred, secret, imported)) recovered.Save(recovered.Loaded);
            using (var transferredRead = EncryptedVault.Open(transferred, secret))
                Require(transferredRead.Loaded.Notes[0].Text == snapshot.Notes[0].Text, "copied file recovery without DPAPI failed");
            var wrong = RandomNumberGenerator.GetBytes(32);
            ExpectFailure(() => EncryptedVault.Open(root, wrong).Dispose(), "wrong secret must fail");
            CryptographicOperations.ZeroMemory(wrong);
            Console.WriteLine("PASS: real encrypted files, recovery-only restart, wrong secret, single writer, plaintext/key scan");
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
            Directory.Delete(root, true);
        }
    }
    internal static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void ExpectFailure(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception e) when (e is IOException or CryptographicException or InvalidDataException or ArgumentException or InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }
    private static bool Contains(byte[] bytes, byte[] target) => bytes.AsSpan().IndexOf(target) >= 0;
}

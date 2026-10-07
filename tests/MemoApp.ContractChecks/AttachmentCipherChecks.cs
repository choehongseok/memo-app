using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Storage;

internal static class AttachmentCipherChecks
{
    private delegate StoredAttachmentObject Seal(ReadOnlySpan<byte> plaintext, Guid vaultId, Guid rootId, ReadOnlySpan<byte> rootKey, string name, string mime);
    private delegate byte[] Open(StoredAttachmentObject value, Guid vaultId, Guid rootId, ReadOnlySpan<byte> rootKey);
    internal static void Run()
    {
        var type = typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Storage.AttachmentObjectCodec");
        VaultChecks.Require(type is not null, "Internal bounded attachment cipher is missing");
        var encrypt = type!.GetMethod("Encrypt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.CreateDelegate<Seal>();
        var decrypt = type.GetMethod("Decrypt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.CreateDelegate<Open>();
        var vault = Guid.Parse("12345678-1234-4321-8765-123456789abc");
        var root = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        var key = RandomNumberGenerator.GetBytes(32);
        var originalKey = key.ToArray();
        try
        {
            foreach (int length in new[] { 0, 1, 65536, 65537, 4194304 })
            {
                var plaintext = RandomNumberGenerator.GetBytes(length);
                var copy = plaintext.ToArray();
                try
                {
                    var value = encrypt(plaintext, vault, root, key, "합성.original", "application/octet-stream");
                    VaultChecks.Require(value.Length == length && value.RootId == root && value.ObjectId != Guid.Empty && value.Name == "합성.original" && value.Mime == "application/octet-stream", "Original metadata must be exact");
                    VaultChecks.Require(value.Sha256 == Convert.ToHexStringLower(SHA256.HashData(plaintext)), "Original SHA256 must be exact");
                    VaultChecks.Require(value.Chunks.Length == Math.Max(1, (length + 65535) / 65536), "Fixed padded chunk count including empty object");
                    var restored = decrypt(value, vault, root, key);
                    try { VaultChecks.Require(restored.SequenceEqual(plaintext), "Attachment bytes must roundtrip before any file/UI integration"); }
                    finally { CryptographicOperations.ZeroMemory(restored); }
                    var dek = IndependentUnwrap(value, vault, root, key);
                    try
                    {
                        for (int index = 0; index < value.Chunks.Length; index++)
                        {
                            var padded = IndependentChunk(value, vault, root, dek, index);
                            try
                            {
                                int take = Math.Min(65536, Math.Max(0, length - index * 65536));
                                VaultChecks.Require(padded.AsSpan(0, take).SequenceEqual(plaintext.AsSpan(index * 65536, take)), "Independent BCL reader verifies exact BE nonce/AAD/HKDF and original bytes");
                                VaultChecks.Require(padded.AsSpan(take).IndexOfAnyExcept((byte)0) < 0, "Padding must be authenticated zero bytes");
                            }
                            finally { CryptographicOperations.ZeroMemory(padded); }
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(dek); }
                    VaultChecks.Require(plaintext.SequenceEqual(copy) && key.SequenceEqual(originalKey), "Cipher does not mutate borrowed plaintext/root buffers");
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(copy); }
            }

            var body = Encoding.UTF8.GetBytes(new string('X', 70000) + " SYNTHETIC_ATTACHMENT_ONLY");
            try
            {
                var value = encrypt(body, vault, root, key, "合成.bin", "application/octet-stream");
                var another = encrypt(body, vault, root, key, "合成.bin", "application/octet-stream");
                VaultChecks.Require(value.ObjectId != another.ObjectId && value.WrappedKey != another.WrappedKey && value.Chunks[0] != another.Chunks[0], "Every attempt generates a fresh object identity, DEK and random wrapping nonce");
                var firstKey = IndependentUnwrap(value, vault, root, key); var secondKey = IndependentUnwrap(another, vault, root, key);
                try { VaultChecks.Require(!CryptographicOperations.FixedTimeEquals(firstKey, secondKey), "Independent unwrap verifies distinct randomly generated object DEKs"); }
                finally { CryptographicOperations.ZeroMemory(firstKey); CryptographicOperations.ZeroMemory(secondKey); }
                VaultChecks.Require(!Convert.FromBase64String(value.WrappedKey).AsSpan(0, 12).SequenceEqual(Convert.FromBase64String(another.WrappedKey).AsSpan(0, 12)), "Distinct wrapping nonce samples");
                VaultChecks.Require(value.Chunks.All(c => Convert.FromBase64String(c).AsSpan().IndexOf(body.AsSpan(0, 32)) < 0), "Ciphertext does not contain synthetic plaintext prefix");
                foreach (int offset in new[] { 0, 12, 59 }) Reject(value with { WrappedKey = Flip(value.WrappedKey, offset) }, "wrap nonce/cipher/tag mutation");
                foreach (int index in new[] { 0, 1 }) foreach (int offset in new[] { 0, 65551 }) Reject(value with { Chunks = value.Chunks.SetItem(index, Flip(value.Chunks[index], offset)) }, "each chunk cipher/tag mutation");
                Reject(value with { ObjectId = Guid.NewGuid() }, "object identity bound to KEK/AAD");
                Reject(value with { RootId = Guid.NewGuid() }, "object root mismatch");
                Reject(value with { Length = value.Length - 1 }, "original length bound to both wraps and chunks");
                Reject(value with { Sha256 = new string('0', 64) }, "final complete original hash validation");
                Reject(value with { Chunks = value.Chunks.Reverse().ToImmutableArray() }, "chunk position/count bound to nonce/AAD");
                Reject(value with { Chunks = value.Chunks.RemoveAt(1) }, "no missing chunk or partial plaintext");
                Reject(value with { WrappedKey = "short" }, "exact wrap encoded-size preflight");
                Reject(value with { Chunks = value.Chunks.SetItem(0, new string('A', 87404)) }, "canonical exact chunk encoded/decoded length");
                VaultChecks.ExpectFailure(() => decrypt(value, Guid.NewGuid(), root, key), "foreign vault must fail");
                VaultChecks.ExpectFailure(() => decrypt(value, vault, Guid.NewGuid(), key), "foreign root must fail");
                var wrong = RandomNumberGenerator.GetBytes(32);
                try { VaultChecks.ExpectFailure(() => decrypt(value, vault, root, wrong), "wrong root key must fail"); }
                finally { CryptographicOperations.ZeroMemory(wrong); }
                var dek = IndependentUnwrap(value, vault, root, key);
                try
                {
                    var padded = IndependentChunk(value, vault, root, dek, 1);
                    try
                    {
                        padded[value.Length - 65536] = 1;
                        var packed = Convert.FromBase64String(value.Chunks[1]);
                        Span<byte> nonce = stackalloc byte[12]; nonce.Clear(); BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], 1);
                        using (var cipher = new AesGcm(dek, 16)) cipher.Encrypt(nonce, padded, packed.AsSpan(0, 65536), packed.AsSpan(65536), ChunkAad(value, vault, root, 1));
                        Reject(value with { Chunks = value.Chunks.SetItem(1, Convert.ToBase64String(packed)) }, "valid tag still refuses nonzero padding before return");
                    }
                    finally { CryptographicOperations.ZeroMemory(padded); }
                }
                finally { CryptographicOperations.ZeroMemory(dek); }
                var returned = decrypt(value, vault, root, key);
                try { returned[0] ^= 1; var unchanged = decrypt(value, vault, root, key); try { VaultChecks.Require(unchanged.SequenceEqual(body), "Returned plaintext is caller-owned and does not alias immutable stored objects"); } finally { CryptographicOperations.ZeroMemory(unchanged); } }
                finally { CryptographicOperations.ZeroMemory(returned); }
                void Reject(StoredAttachmentObject altered, string reason)
                {
                    byte[]? leaked = null;
                    VaultChecks.ExpectFailure(() => leaked = decrypt(altered, vault, root, key), reason);
                    VaultChecks.Require(leaked is null, "No plaintext is returned before all chunks/padding/hash authenticate");
                }
            }
            finally { CryptographicOperations.ZeroMemory(body); }
            VaultChecks.ExpectFailure(() => encrypt(new byte[4194305], vault, root, key, "large.bin", "application/octet-stream"), "object4MiB preflight");
            VaultChecks.ExpectFailure(() => encrypt([], vault, root, key, "../unsafe.bin", "application/octet-stream"), "metadata cannot become path");
            VaultChecks.ExpectFailure(() => encrypt([], vault, root, key, "safe.bin", "text/plain\n"), "metadata strict MIME");
            VaultChecks.ExpectFailure(() => encrypt([], Guid.Empty, root, key, "safe.bin", "text/plain"), "empty vault refused");
            VaultChecks.ExpectFailure(() => encrypt([], vault, Guid.Empty, key, "safe.bin", "text/plain"), "empty root refused");
            VaultChecks.ExpectFailure(() => encrypt([], vault, root, [], "safe.bin", "text/plain"), "wrong key length refused");
            Console.WriteLine("PASS: internal bounded attachment AES-GCM/HKDF roundtrip, independent BE format reader, fresh object/DEK/wrap nonce, full tamper/padding/hash/refusal and owned-return checks (not anchored/persisted attachment functionality)");
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(originalKey); }
    }
    private static string Flip(string encoded, int index) { var bytes = Convert.FromBase64String(encoded); bytes[index] ^= 1; return Convert.ToBase64String(bytes); }
    private static byte[] Context(string domain, Guid vault, Guid root, Guid id, params byte[][] suffix)
    {
        using var output = new MemoryStream(); output.Write(Encoding.ASCII.GetBytes(domain)); output.Write([0, 0, 0, 1]);
        output.Write(vault.ToByteArray(true)); output.Write(root.ToByteArray(true)); output.Write(id.ToByteArray(true));
        foreach (var bytes in suffix) output.Write(bytes); return output.ToArray();
    }
    private static byte[] U32(int value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); return bytes; }
    private static byte[] U64(int value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)value)); return bytes; }
    private static byte[] ChunkAad(StoredAttachmentObject value, Guid vault, Guid root, int index) => Context("MEMOAPP-ATTACH-CHUNK", vault, root, value.ObjectId, U32(index), U32(value.Chunks.Length), U64(value.Length));
    private static byte[] IndependentUnwrap(StoredAttachmentObject value, Guid vault, Guid root, byte[] key)
    {
        var kek = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, root.ToByteArray(true), Context("MEMOAPP-ATTACH-KEK", vault, root, value.ObjectId));
        var packed = Convert.FromBase64String(value.WrappedKey); var dek = new byte[32];
        try { using var cipher = new AesGcm(kek, 16); cipher.Decrypt(packed.AsSpan(0, 12), packed.AsSpan(12, 32), packed.AsSpan(44, 16), dek, Context("MEMOAPP-ATTACH-WRAP", vault, root, value.ObjectId, U64(value.Length), U32(value.Chunks.Length))); return dek; }
        catch { CryptographicOperations.ZeroMemory(dek); throw; }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }
    private static byte[] IndependentChunk(StoredAttachmentObject value, Guid vault, Guid root, byte[] dek, int index)
    {
        var packed = Convert.FromBase64String(value.Chunks[index]); var plaintext = new byte[65536];
        Span<byte> nonce = stackalloc byte[12]; nonce.Clear(); BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], checked((uint)index));
        try { using var cipher = new AesGcm(dek, 16); cipher.Decrypt(nonce, packed.AsSpan(0, 65536), packed.AsSpan(65536, 16), plaintext, ChunkAad(value, vault, root, index)); return plaintext; }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }
}

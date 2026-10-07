using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace MemoApp.Core.Storage;

// Internal primitive only. The vault owner must durably anchor its root before calling Encrypt.
// There is deliberately no caller-provided object ID, key, nonce or re-encryption API.
internal static class AttachmentObjectCodec
{
    internal static StoredAttachmentObject Encrypt(ReadOnlySpan<byte> plaintext, Guid vaultId, Guid rootId, ReadOnlySpan<byte> rootKey, string name, string mime)
    {
        Identity(vaultId, rootId, rootKey);
        if (plaintext.Length > AttachmentValidation.MaxObject) throw new InvalidDataException("Attachment byte budget");
        var hash = Convert.ToHexStringLower(SHA256.HashData(plaintext));
        AttachmentValidation.Description(name, mime, plaintext.Length, hash);
        var objectId = Guid.NewGuid();
        int count = AttachmentValidation.ChunkCount(plaintext.Length);
        Span<byte> dek = stackalloc byte[32], kek = stackalloc byte[32];
        var padded = new byte[AttachmentValidation.ChunkSize];
        try
        {
            RandomNumberGenerator.Fill(dek);
            Derive(kek, rootKey, vaultId, rootId, objectId);
            Span<byte> wrapped = stackalloc byte[60];
            RandomNumberGenerator.Fill(wrapped[..12]);
            using (var wrap = new AesGcm(kek, 16)) wrap.Encrypt(wrapped[..12], dek, wrapped.Slice(12, 32), wrapped.Slice(44, 16), WrapAad(vaultId, rootId, objectId, plaintext.Length, count));
            var chunks = ImmutableArray.CreateBuilder<string>(count);
            var packed = new byte[AttachmentValidation.ChunkSize + 16];
            Span<byte> nonce = stackalloc byte[12];
            using var cipher = new AesGcm(dek, 16);
            for (int index = 0; index < count; index++)
            {
                padded.AsSpan().Clear();
                int offset = index * AttachmentValidation.ChunkSize;
                int take = Math.Min(AttachmentValidation.ChunkSize, plaintext.Length - offset);
                plaintext.Slice(offset, take).CopyTo(padded);
                ChunkNonce(nonce, index);
                cipher.Encrypt(nonce, padded, packed.AsSpan(0, AttachmentValidation.ChunkSize), packed.AsSpan(AttachmentValidation.ChunkSize), ChunkAad(vaultId, rootId, objectId, index, count, plaintext.Length));
                chunks.Add(Convert.ToBase64String(packed));
            }
            return new(objectId, rootId, name, mime, plaintext.Length, hash, Convert.ToBase64String(wrapped), chunks.MoveToImmutable());
        }
        finally { CryptographicOperations.ZeroMemory(dek); CryptographicOperations.ZeroMemory(kek); CryptographicOperations.ZeroMemory(padded); }
    }

    // Returned plaintext belongs to the caller, who must zero it. No partial return on any failure.
    internal static byte[] Decrypt(StoredAttachmentObject value, Guid vaultId, Guid rootId, ReadOnlySpan<byte> rootKey)
    {
        Identity(vaultId, rootId, rootKey);
        AttachmentValidation.Object(value, rootId); // exact encoded and decoded bounds before allocation
        Span<byte> dek = stackalloc byte[32], kek = stackalloc byte[32];
        var padded = new byte[AttachmentValidation.ChunkSize];
        var result = new byte[value.Length];
        bool returned = false;
        try
        {
            Derive(kek, rootKey, vaultId, rootId, value.ObjectId);
            var wrapped = Convert.FromBase64String(value.WrappedKey);
            using (var wrap = new AesGcm(kek, 16)) wrap.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(12, 32), wrapped.AsSpan(44, 16), dek, WrapAad(vaultId, rootId, value.ObjectId, value.Length, value.Chunks.Length));
            Span<byte> nonce = stackalloc byte[12];
            using var cipher = new AesGcm(dek, 16);
            for (int index = 0; index < value.Chunks.Length; index++)
            {
                var packed = Convert.FromBase64String(value.Chunks[index]);
                ChunkNonce(nonce, index);
                cipher.Decrypt(nonce, packed.AsSpan(0, AttachmentValidation.ChunkSize), packed.AsSpan(AttachmentValidation.ChunkSize, 16), padded, ChunkAad(vaultId, rootId, value.ObjectId, index, value.Chunks.Length, value.Length));
                int offset = index * AttachmentValidation.ChunkSize;
                int take = Math.Min(AttachmentValidation.ChunkSize, value.Length - offset);
                if (padded.AsSpan(take).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("Attachment padding");
                padded.AsSpan(0, take).CopyTo(result.AsSpan(offset, take));
            }
            Span<byte> actualHash = stackalloc byte[32];
            SHA256.HashData(result, actualHash);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(value.Sha256))) throw new InvalidDataException("Attachment original hash");
            returned = true;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek); CryptographicOperations.ZeroMemory(kek); CryptographicOperations.ZeroMemory(padded);
            if (!returned) CryptographicOperations.ZeroMemory(result);
        }
    }
    private static void Identity(Guid vault, Guid root, ReadOnlySpan<byte> key)
    { if (vault == Guid.Empty || root == Guid.Empty || key.Length != 32) throw new InvalidDataException("Attachment key identity"); }
    private static void Derive(Span<byte> output, ReadOnlySpan<byte> rootKey, Guid vault, Guid root, Guid id)
    { HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey, output, root.ToByteArray(true), Context("MEMOAPP-ATTACH-KEK", vault, root, id)); }
    private static byte[] Context(string domain, Guid vault, Guid root, Guid id)
    {
        var prefix = Encoding.ASCII.GetBytes(domain);
        var bytes = new byte[prefix.Length + 4 + 48];
        prefix.CopyTo(bytes, 0); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(prefix.Length, 4), 1);
        vault.TryWriteBytes(bytes.AsSpan(prefix.Length + 4, 16), true, out _);
        root.TryWriteBytes(bytes.AsSpan(prefix.Length + 20, 16), true, out _);
        id.TryWriteBytes(bytes.AsSpan(prefix.Length + 36, 16), true, out _);
        return bytes;
    }
    private static byte[] WrapAad(Guid vault, Guid root, Guid id, int length, int count)
    {
        var context = Context("MEMOAPP-ATTACH-WRAP", vault, root, id); var bytes = new byte[context.Length + 12]; context.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(context.Length, 8), checked((ulong)length)); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(context.Length + 8, 4), checked((uint)count)); return bytes;
    }
    private static byte[] ChunkAad(Guid vault, Guid root, Guid id, int index, int count, int length)
    {
        var context = Context("MEMOAPP-ATTACH-CHUNK", vault, root, id); var bytes = new byte[context.Length + 16]; context.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(context.Length, 4), checked((uint)index)); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(context.Length + 4, 4), checked((uint)count)); BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(context.Length + 8, 8), checked((ulong)length)); return bytes;
    }
    private static void ChunkNonce(Span<byte> nonce, int index)
    { nonce.Clear(); BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], checked((uint)index)); }
}

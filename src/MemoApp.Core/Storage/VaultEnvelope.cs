using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace MemoApp.Core.Storage;

internal sealed record EnvelopeHeader(Guid VaultId, Guid Epoch, Guid SnapshotId, ulong Sequence, ulong WrapCount, int PayloadLength);
internal static class VaultEnvelope
{
    internal const int MaxFile = 16 * 1024 * 1024;
    internal const ulong MaxWraps = 1UL << 20;
    internal const int HeaderSize = 84;
    private static readonly byte[] Magic = "MEMOV001"u8.ToArray();
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    internal static byte[] Encrypt(byte[] plaintext, byte[] vaultKey, byte[] recoveryKey, EnvelopeHeader header)
    {
        if (vaultKey.Length != 32 || recoveryKey.Length != 32 || header.PayloadLength != plaintext.Length) throw new InvalidDataException("Invalid key or payload size");
        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var aad = WriteHeader(header);
            using var output = new MemoryStream();
            output.Write(aad);
            Seal(output, recoveryKey, vaultKey, [.. aad, 1]);
            Seal(output, vaultKey, dataKey, [.. aad, 2]);
            Seal(output, dataKey, plaintext, [.. aad, 3]);
            return output.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(dataKey); }
    }
    private static void Seal(Stream output, byte[] key, byte[] plaintext, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var cipher = new AesGcm(key, 16);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        output.Write(nonce); output.Write(ciphertext); output.Write(tag);
    }
    internal static (EnvelopeHeader Header, VaultSnapshot Snapshot) Decrypt(byte[] bytes, byte[] recoveryKey)
    {
        if (recoveryKey.Length != 32) throw new ArgumentException("Recovery secret must be 32 bytes");
        var header = ParseHeader(bytes);
        byte[]? vaultKey = null, dataKey = null, plaintext = null;
        try
        {
            byte[] aad = bytes[..HeaderSize];
            vaultKey = Open(bytes, HeaderSize, 32, recoveryKey, [.. aad, 1]);
            dataKey = Open(bytes, HeaderSize + 60, 32, vaultKey, [.. aad, 2]);
            plaintext = Open(bytes, HeaderSize + 120, header.PayloadLength, dataKey, [.. aad, 3]);
            using var document = JsonDocument.Parse(plaintext, new() { MaxDepth = 16 });
            CheckDuplicates(document.RootElement);
            foreach (var required in new[] { "schemaVersion", "deviceId", "notes", "history", "tombstones" })
                if (!document.RootElement.TryGetProperty(required, out _)) throw new InvalidDataException("Missing schema field");
            if (document.RootElement.GetProperty("notes").GetArrayLength() > 100 ||
                document.RootElement.GetProperty("history").ValueKind != JsonValueKind.Array || document.RootElement.GetProperty("history").GetArrayLength() > 10000 ||
                document.RootElement.GetProperty("tombstones").GetArrayLength() > 100) throw new InvalidDataException("Record limit exceeded");
            var snapshot = JsonSerializer.Deserialize<VaultSnapshot>(plaintext, JsonOptions) ?? throw new InvalidDataException("Missing snapshot");
            Validate(snapshot);
            return (header, snapshot);
        }
        catch (JsonException) { throw new InvalidDataException("Invalid snapshot JSON"); }
        finally
        {
            if (vaultKey is not null) CryptographicOperations.ZeroMemory(vaultKey);
            if (dataKey is not null) CryptographicOperations.ZeroMemory(dataKey);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
    private static byte[] Open(byte[] bytes, int offset, int length, byte[] key, byte[] aad)
    {
        var result = new byte[length];
        try
        {
            using var cipher = new AesGcm(key, 16);
            cipher.Decrypt(bytes.AsSpan(offset, 12), bytes.AsSpan(offset + 12, length), bytes.AsSpan(offset + 12 + length, 16), result, aad);
            return result;
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
    }
    internal static EnvelopeHeader ParseHeader(byte[] bytes)
    {
        if (bytes.Length < HeaderSize + 148 || bytes.Length > MaxFile) throw new InvalidDataException("Envelope size outside limits");
        using var input = new BinaryReader(new MemoryStream(bytes, false));
        if (!input.ReadBytes(8).SequenceEqual(Magic) || input.ReadUInt32() != 1 || input.ReadUInt32() != 1) throw new InvalidDataException("Unsupported envelope");
        var vault = new Guid(input.ReadBytes(16), true);
        var epoch = new Guid(input.ReadBytes(16), true);
        var snapshot = new Guid(input.ReadBytes(16), true);
        ulong sequence = input.ReadUInt64(), wraps = input.ReadUInt64();
        uint size = input.ReadUInt32();
        if (vault == Guid.Empty || epoch == Guid.Empty || snapshot == Guid.Empty || sequence == 0 || wraps < 2 || wraps > MaxWraps || size == 0 || size > MaxFile - HeaderSize - 148)
            throw new InvalidDataException("Invalid envelope fields");
        if (checked((long)HeaderSize + 148 + size) != bytes.Length) throw new InvalidDataException("Truncated or trailing envelope bytes");
        return new(vault, epoch, snapshot, sequence, wraps, checked((int)size));
    }
    private static byte[] WriteHeader(EnvelopeHeader h)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write(Magic); writer.Write(1U); writer.Write(1U);
        writer.Write(h.VaultId.ToByteArray(true)); writer.Write(h.Epoch.ToByteArray(true)); writer.Write(h.SnapshotId.ToByteArray(true));
        writer.Write(h.Sequence); writer.Write(h.WrapCount); writer.Write(checked((uint)h.PayloadLength));
        var result = bytes.ToArray();
        if (h.PayloadLength <= 0 || h.PayloadLength > MaxFile - HeaderSize - 148 || h.WrapCount > MaxWraps || h.Sequence == 0) throw new InvalidDataException("Envelope budget exceeded");
        return result;
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate JSON property");
                CheckDuplicates(p.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
    internal static void Validate(VaultSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1 || snapshot.DeviceId == Guid.Empty || snapshot.Notes is null || snapshot.Notes.Length > 100 || snapshot.History is null || snapshot.History.Length > 10000 || snapshot.Tombstones is null || snapshot.Tombstones.Length > 100) throw new InvalidDataException("Unsupported snapshot or note limit");
        var ids = new HashSet<Guid>();
        var revisions = new HashSet<Guid>();
        var graph = new Dictionary<Guid, (Guid NoteId, Guid[] Parents)>();
        foreach (var note in snapshot.Notes)
        {
            if (note is null || note.NoteId == Guid.Empty || !ids.Add(note.NoteId) || note.RevisionId == Guid.Empty || !revisions.Add(note.RevisionId) ||
                note.Title is null || note.Text is null || note.Title.Length > 256 || note.Text.Length > 65536 || note.Parents is null || note.Parents.Length > 8 ||
                note.Parents.Any(p => p == Guid.Empty || p == note.RevisionId) || note.Parents.Distinct().Count() != note.Parents.Length ||
                note.Mode != "plain" || note.Scope != "device-only" || note.CreatedAt.Offset != TimeSpan.Zero || note.ModifiedAt.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Invalid note fields or unsupported mode");
            graph.Add(note.RevisionId, (note.NoteId, note.Parents));
        }
        foreach (var revision in snapshot.History)
        {
            if (revision is null || !ids.Contains(revision.NoteId) || revision.RevisionId == Guid.Empty || !revisions.Add(revision.RevisionId) ||
                revision.Parents is null || revision.Parents.Length > 8 || revision.Title is null || revision.Title.Length > 256 ||
                revision.Text is null || revision.Text.Length > 65536 || revision.ModifiedAt.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Invalid history");
            graph.Add(revision.RevisionId, (revision.NoteId, revision.Parents));
        }
        if (snapshot.History.GroupBy(r => r.NoteId).Any(g => g.Count() > 512)) throw new InvalidDataException("History limit exceeded");
        var deletedIds = new HashSet<Guid>();
        foreach (var deleted in snapshot.Tombstones)
        {
            if (deleted is null || deleted.NoteId == Guid.Empty || ids.Contains(deleted.NoteId) || !deletedIds.Add(deleted.NoteId) ||
                deleted.RevisionId == Guid.Empty || !revisions.Add(deleted.RevisionId) || deleted.Parents is null || deleted.Parents.Length != 0)
                throw new InvalidDataException("Unsupported or invalid tombstone");
        }
        foreach (var pair in graph)
        {
            var parents = pair.Value.Parents;
            if (parents.Distinct().Count() != parents.Length || parents.Any(p => p == pair.Key || !graph.TryGetValue(p, out var parent) || parent.NoteId != pair.Value.NoteId))
                throw new InvalidDataException("Invalid revision relation");
        }
        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        void Visit(Guid revision)
        {
            if (visited.Contains(revision)) return;
            if (!visiting.Add(revision)) throw new InvalidDataException("Revision cycle");
            foreach (var parent in graph[revision].Parents) Visit(parent);
            visiting.Remove(revision); visited.Add(revision);
        }
        foreach (var revision in graph.Keys) Visit(revision);
    }
}

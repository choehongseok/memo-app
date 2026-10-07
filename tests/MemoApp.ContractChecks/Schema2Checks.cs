using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class Schema2Checks
{
    internal static async Task Run()
    {
        var workspace = new EditingWorkspace(TimeProvider.System);
        var folder = workspace.CreateFolder("合성 비공개 폴더 ONLY_TEST");
        var note = workspace.CreateNote(); note.Title = "합성 schema2"; note.Text = "SCHEMA2_PRIVATE_SYNTHETIC";
        workspace.MoveNote(note, folder.FolderId); workspace.SetTags(note, ["비공개 태그 ONLY_TEST"]);
        var snapshot = workspace.Capture();
        VaultEnvelope.Validate(snapshot);
        var secret = EncryptedVault.GenerateRecoverySecret();
        var root = Path.Combine(Path.GetTempPath(), "memo-schema2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using (var vault = EncryptedVault.Create(root, secret, secret)) vault.Save(snapshot);
            using (var vault = EncryptedVault.Open(root, secret))
                VaultChecks.Require(vault.Loaded.SchemaVersion == 2 && vault.Loaded.Folders.Single().Name == folder.Name && vault.Loaded.Tags.Length == 1, "organization encrypted restart");
            var validJson = JsonSerializer.SerializeToUtf8Bytes(snapshot, VaultEnvelope.JsonOptions);
            void Reject(JsonObject altered, string reason) => VaultChecks.ExpectFailure(() => Decode(altered, secret), reason);
            JsonObject Fresh() => JsonNode.Parse(validJson)!.AsObject();
            var missing = Fresh(); missing.Remove("folders"); Reject(missing, "schema2 missing folders must reject");
            missing = Fresh(); missing["notes"]![0]!.AsObject().Remove("metadata"); Reject(missing, "schema2 missing metadata must reject");
            missing = Fresh(); missing["notes"]![0]!["metadata"]!.AsObject().Remove("deleted"); Reject(missing, "schema2 missing metadata flag must reject");
            missing = Fresh(); missing["notes"]![0]!["metadata"]!["tagIds"] = null; Reject(missing, "schema2 null tag IDs must reject");
            missing = Fresh(); missing["notes"]![0]!["metadata"]!["color"] = "unsupported"; Reject(missing, "unsupported colors reject");
            missing = Fresh(); missing["notes"]![0]!["metadata"]!["folderId"] = Guid.NewGuid().ToString(); Reject(missing, "unknown folder reference reject");
            missing = Fresh(); missing["folders"]![0]!["parentId"] = folder.FolderId.ToString(); Reject(missing, "folder cycle reject");
            missing = Fresh(); missing["notes"]![0]!["metadata"]!["deleted"] = true; Reject(missing, "deleted note needs matching tombstone");
            missing = Fresh(); missing["schemaVersion"] = 99; Reject(missing, "unknown schema writes/read reject");
            missing = Fresh(); missing["notes"]![0]!["unknown"] = true; Reject(missing, "unmapped schema2 note field reject");
            workspace.AcceptPrepared(snapshot); workspace.DeleteNote(note);
            var deleted = workspace.Capture(); VaultEnvelope.Validate(deleted); workspace.AcceptPrepared(deleted);
            workspace.RestoreNote(note); var restored = workspace.Capture(); VaultEnvelope.Validate(restored);
            var historyTags = restored.History[0].Metadata.TagIds;
            workspace.SetTags(note, ["바뀐태그"]);
            VaultChecks.Require(restored.History[0].Metadata.TagIds.SequenceEqual(historyTags), "tag edits must not mutate immutable history");
            // Generate an actual legacy payload with no new properties, rather than a schema2-shaped object labeled v1.
            var legacy = new VaultSnapshot(1, Guid.NewGuid(), [snapshot.Notes[0] with { Metadata = new() }]);
            var legacyJson = JsonSerializer.SerializeToNode(legacy, VaultEnvelope.JsonOptions)!.AsObject();
            legacyJson.Remove("folders"); legacyJson.Remove("tags"); legacyJson["notes"]![0]!.AsObject().Remove("metadata");
            var legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
            var original = Encode(legacyJson, secret); File.WriteAllBytes(Path.Combine(legacyRoot, "current.vault"), original);
            using (var session = new SaveCoordinator(EncryptedVault.Open(legacyRoot, secret), TimeProvider.System))
            {
                VaultChecks.Require(session.Workspace.Notes.Single().Text == note.Text && session.IsDirty, "v1 loads losslessly and marks migration pending");
                VaultChecks.Require(await session.SaveAsync(), "v1 migration save");
                await session.LockAsync();
            }
            var previous = Directory.GetFiles(legacyRoot, "previous-*.vault");
            VaultChecks.Require(previous.Length == 1 && File.ReadAllBytes(previous[0]).SequenceEqual(original), "first v2 write must preserve exact legacy bytes");
            using (var reopened = EncryptedVault.Open(legacyRoot, secret)) VaultChecks.Require(reopened.Loaded.SchemaVersion == 2, "migrated schema restart");
            var failingRoot = Path.Combine(root, "failed-migration"); Directory.CreateDirectory(failingRoot); File.WriteAllBytes(Path.Combine(failingRoot, "current.vault"), original);
            using (var session = new SaveCoordinator(EncryptedVault.Open(failingRoot, secret, files: new VaultFailureChecks.FaultFiles("pre-flush")), TimeProvider.System))
            {
                VaultChecks.Require(!await session.SaveAsync(), "migration fault must report failure"); await session.LockAsync();
            }
            VaultChecks.Require(File.ReadAllBytes(Path.Combine(failingRoot, "current.vault")).SequenceEqual(original), "failed migration must preserve legacy current");
            foreach (var file in Directory.GetFiles(root, "*.vault", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(file);
                VaultChecks.Require(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(folder.Name)) < 0 && bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(note.Text)) < 0 && bytes.AsSpan().IndexOf(secret) < 0, "schema2 plaintext/key disk leak");
            }
            Console.WriteLine("PASS: schema2 strict fields/relations, encrypted organization/restart, v1 exact-byte migration/failed migration, immutable metadata and plaintext scan");
        }
        finally { CryptographicOperations.ZeroMemory(secret); Directory.Delete(root, true); }
    }
    internal static byte[] Encode(JsonObject payload, byte[] secret)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString()); var key = RandomNumberGenerator.GetBytes(32);
        try { return VaultEnvelope.Encrypt(json, key, secret, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 2, json.Length)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(json); }
    }
    private static void Decode(JsonObject payload, byte[] secret) => _ = VaultEnvelope.Decrypt(Encode(payload, secret), secret);
}

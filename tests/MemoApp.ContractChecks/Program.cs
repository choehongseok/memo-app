using System.Text.Json;
using MemoApp.Core;

if (VaultFailureChecks.TryWorker(args)) return;

// Synthetic in-memory contract checks; these do not prove save/encryption/sync behavior.
using var content = JsonDocument.Parse("""
{"nodes":[{"type":"paragraph","text":"가상 한글 메모"},{"type":"table","cells":[["합성"]]},
{"type":"image","attachmentId":"00000000-0000-0000-0000-000000000009"},
{"type":"future-node","version":99,"opaque":{"keep":true}}]}
""");
var note = new DocumentContract(1, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()],
    Guid.NewGuid(), "합성 제목", "rich", "device-only", false, content.RootElement.Clone(),
    [Guid.Parse("00000000-0000-0000-0000-000000000009")]);
var json = JsonSerializer.Serialize(note);
var restored = JsonSerializer.Deserialize<DocumentContract>(json)
    ?? throw new InvalidOperationException("Null document");
if (restored.NoteId != note.NoteId || restored.RevisionId != note.RevisionId ||
    !restored.ParentRevisionIds.SequenceEqual(note.ParentRevisionIds) ||
    !restored.AttachmentIds.SequenceEqual(note.AttachmentIds) ||
    restored.Scope != note.Scope || restored.Title != note.Title || restored.Mode != note.Mode ||
    restored.SchemaVersion != note.SchemaVersion || restored.DeviceId != note.DeviceId ||
    restored.Deleted != note.Deleted || !JsonElement.DeepEquals(restored.Content, note.Content))
    throw new InvalidOperationException("Contract roundtrip lost data");
if (restored.Content.GetProperty("nodes")[3].GetProperty("opaque").GetProperty("keep").GetBoolean() != true)
    throw new InvalidOperationException("Unknown node lost");
Console.WriteLine("PASS: synthetic Korean/table/image/unknown-node contract roundtrip (in-memory only)");
EditingChecks.Run();
VaultChecks.Run();
VaultFailureChecks.Run();
await CoordinatorChecks.Run();

using System.Text.Json;
using MemoApp.Core;

if (args.Contains("--selected-png-only")) { await SelectedPngChecks.Run(); return; }
if (LocalIdentityCreationChecks.TryWorker(args)) return;
if (args.Contains("--identity-only")) { await LocalIdentityCreationChecks.Run(); return; }
if (VaultFailureChecks.TryWorker(args)) return;
if (AttachmentSourceChecks.TryWorker(args)) return;
if (args.Contains("--markdown-only")) { MarkdownChecks.Run(); return; }
if (args.Contains("--markdown-queue-only")) { await MarkdownQueueChecks.Run(); return; }
if (args.Contains("--content-version-only")) { ContentVersionChecks.Run(); return; }
if (args.Contains("--attachment-contract-only")) { AttachmentContractChecks.Run(); return; }
if (args.Contains("--attachment-cipher-only")) { AttachmentCipherChecks.Run(); return; }
if (args.Contains("--attachment-envelope-only")) { AttachmentEnvelopeChecks.Run(); return; }
if (args.Contains("--attachment-root-only")) { await AttachmentRootChecks.Run(); return; }
if (args.Contains("--attachment-mutation-only")) { await AttachmentMutationChecks.Run(); return; }
if (args.Contains("--attachment-source-only")) { AttachmentSourceChecks.Run(); return; }
if (args.Contains("--attachment-metadata-only")) { AttachmentMetadataChecks.Run(); return; }
if (args.Contains("--png-profile-only")) { PngProfileChecks.Run(); return; }
if (args.Contains("--png-decoder-only")) { PngDecoderChecks.Run(); return; }
if (args.Contains("--attachment-lease-only")) { await AttachmentLeaseChecks.Run(); return; }
if (args.Contains("--org-preflight-only")) { OrganizationPreflightChecks.Run(); return; }
if (args.Contains("--rich-only")) { RichDocumentChecks.Run(); return; }
if (args.Contains("--rich-store-only")) { await RichStorageChecks.Run(); return; }
if (args.Contains("--batch-only")) { await BatchChecks.Run(); return; }
if (args.Contains("--diff-only")) { HistoryDiffChecks.Run(); return; }
if (args.Contains("--devices-only")) { await DeviceUiChecks.Run(); return; }
if (args.Contains("--startup-only")) { StartupChecks.Run(); return; }
if (args.Contains("--schema-only")) { await Schema2Checks.Run(); return; }
if (args.Contains("--text-only")) { TextTransferChecks.Run(); return; }

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
ContentVersionChecks.Run();
AttachmentContractChecks.Run();
AttachmentCipherChecks.Run();
AttachmentEnvelopeChecks.Run();
await AttachmentRootChecks.Run();
await AttachmentMutationChecks.Run();
AttachmentSourceChecks.Run();
AttachmentMetadataChecks.Run();
PngProfileChecks.Run();
await AttachmentLeaseChecks.Run();
PngDecoderChecks.Run();
RichDocumentChecks.Run();
MarkdownChecks.Run();
await MarkdownQueueChecks.Run();
await RichStorageChecks.Run();
SearchChecks.Run();
OrganizationChecks.Run();
OrganizationPreflightChecks.Run();
await BatchChecks.Run();
HistoryDiffChecks.Run();
await Schema2Checks.Run();
await BackupChecks.Run();
TextTransferChecks.Run();
StartupChecks.Run();
await DeviceUiChecks.Run();
await LocalIdentityCreationChecks.Run();
VaultChecks.Run();
VaultFailureChecks.Run();
await CoordinatorChecks.Run();

await SelectedPngChecks.Run();

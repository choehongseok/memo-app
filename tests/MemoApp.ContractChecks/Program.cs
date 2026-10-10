using System.Text.Json;
using MemoApp.Core;

if (args.Length==2&&args[0]=="--ocr-native-probe") { await LocalOcrChecks.NativeProbe(args[1]); return; }
if (args.Length==2&&args[0]=="--ocr-linux-probe") { await LocalOcrChecks.LinuxProbe(args[1]); return; }
if (LocalOcrChecks.TryWorker(args)) return;
if (args.Contains("--ocr-cleanup-fault-only")) { await OcrCleanupFaultChecks.Run(); return; }
if (args.Contains("--linked-ocr-starter-only")) { await LinkedOcrStarterChecks.Run(); return; }
if (args.Contains("--schema11-ocr-only")) { await Schema11OcrChecks.Run(); return; }
if (args.Contains("--attachment-ocr-grant-only")) { await AttachmentOcrGrantChecks.Run(); return; }
if (args.Contains("--attachment-ocr-input-only")) { await AttachmentOcrInputChecks.Run(); return; }
if (args.Contains("--o06-merge-only")) { await BackupMergeChecks.Run(); return; }
if (args.Contains("--branch-resolution-only")) { await BranchResolutionChecks.Run(); return; }
if (args.Contains("--branch-resolution-owner-only")) { await BranchResolutionLifecycleChecks.Run(); return; }
if (args.Contains("--branch-resolution-all-only")) { await BranchResolutionChecks.Run(); await BranchResolutionLifecycleChecks.Run(); return; }
if (args.Contains("--ocr-process-only")) { await LocalOcrChecks.ProcessChecks(); return; }
if (args.Contains("--trash-encryption-only")) { await TrashEncryptionChecks.Run(); return; }
if (args.Contains("--user-link-only")) { UserLinkChecks.Run(); return; }
if (args.Contains("--png-metadata-only")) { PngMetadataChecks.Run(); return; }
if (args.Contains("--structured-word-only")) { StructuredWordChecks.Run(); return; }
if (args.Contains("--h01-insertion-admission-only")) { await InlineImageAdmissionChecks.Run(); return; }
if (args.Contains("--h01-codec-only")) { await InlineImageChecks.Run(); return; }
if (args.Contains("--h01-schema-only")) { await Schema10ImageChecks.Run(); await Schema10ImageHiddenChecks.Run(); await InlineImageAdmissionChecks.Run(); return; }
if (args.Contains("--h01-image-only")) { await InlineImageChecks.Run(); await Schema10ImageChecks.Run(); await Schema10ImageHiddenChecks.Run(); await InlineImageAdmissionChecks.Run(); return; }
if (args.Contains("--schema9-only")) { await Schema9Checks.Run(); await Schema9HiddenChecks.Run(); return; }
if (args.Contains("--spreadsheet-scalar-only")) { SpreadsheetScalarChecks.Run(); return; }
if (args.Contains("--spreadsheet-scalar-import-only")) { SpreadsheetScalarImportChecks.Run(); return; }
if (args.Contains("--spreadsheet-mapping-only")) { SpreadsheetMappingChecks.Run(); return; }
if (args.Contains("--automatic-trash-only")) { AutomaticTrashChecks.Run(); return; }
if (args.Contains("--trash-lineage-only")) { TrashLineageChecks.Run(); return; }
if (args.Contains("--ocr-input-only")) { LocalOcrChecks.Input(); return; }

if (args.Contains("--search-excerpt-only")) { SearchExcerptChecks.Run(); return; }
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
if (args.Contains("--dib-to-png-only")) { DibToPngChecks.Run(); return; }
if (args.Contains("--bounded-file-only")) { BoundedFileReaderChecks.Run(); return; }
if (args.Contains("--markdown-file-only")) { await MarkdownFileTransferChecks.Run(); return; }
if (args.Contains("--attachment-file-only")) { await AttachmentFileTransferChecks.Run(); return; }
if (args.Contains("--attachment-lease-only")) { await AttachmentLeaseChecks.Run(); return; }
if (args.Contains("--org-preflight-only")) { OrganizationPreflightChecks.Run(); return; }
if (args.Contains("--rich-only")) { RichDocumentChecks.Run(); return; }
if (args.Contains("--rich-store-only")) { await RichStorageChecks.Run(); return; }
if (args.Contains("--batch-only")) { await BatchChecks.Run(); return; }
if (args.Contains("--diff-only")) { HistoryDiffChecks.Run(); return; }
if (args.Contains("--devices-only")) { await DeviceUiChecks.Run(); return; }
if (args.Contains("--startup-only")) { StartupChecks.Run(); StartupRegistrationChecks.Run(); return; }
if (args.Length==2&&args[0]=="--pdf-probe") { PdfTextExportChecks.WriteProbe(args[1]); return; }
if (args.Contains("--pdf-export-only")) { PdfTextExportChecks.Run(); return; }
if (args.Contains("--pdf-raster-only")) { PdfRasterDocumentChecks.Run(); return; }
if (args.Contains("--path-links-only")) { await FilePathLinkChecks.Run(); return; }
if (args.Contains("--schema-only")) { await Schema2Checks.Run(); return; }
if (args.Contains("--device-search-activation-only")) { await DeviceSearchActivationChecks.Run(); return; }
if (args.Contains("--device-search-state-only")) { DeviceSearchStateChecks.Run(); return; }
if (args.Contains("--batch-text-only")) { BatchTextTransferChecks.Run(); return; }
if (args.Contains("--selected-backup-only")) { await SelectedBackupChecks.Run(); return; }
if (args.Contains("--backup-preview-only")) { await BackupPreviewChecks.Run(); return; }
if (args.Contains("--clipboard-png-only")) { ClipboardPngChecks.Run(); await AttachmentPreparationChecks.Run(); return; }
if (args.Contains("--excel-import-only")) { await ExcelImportChecks.Run(); return; }
if (args.Contains("--office-export-only")) { OfficeTextExportChecks.Run(); return; }
if (args.Contains("--whole-migration-only")) { await WholeMigrationChecks.Run(); return; }
if (args.Contains("--auto-backup-only")) { AutomaticBackupPolicyChecks.Run(); return; }
if (args.Contains("--prepared-backup-only")) { await PreparedBackupChecks.Run(); return; }
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
LocalOcrChecks.Input();
await LocalOcrChecks.ProcessChecks();
EditingChecks.Run();
await BackupMergeChecks.Run();
await BranchResolutionChecks.Run();
await BranchResolutionLifecycleChecks.Run();
await AttachmentOcrInputChecks.Run();
await LinkedOcrStarterChecks.Run();
await Schema11OcrChecks.Run();
await AttachmentOcrGrantChecks.Run();
SpreadsheetMappingChecks.Run();
await Schema9Checks.Run();
await Schema9HiddenChecks.Run();
ContentVersionChecks.Run();
AttachmentContractChecks.Run();
AttachmentCipherChecks.Run();
AttachmentEnvelopeChecks.Run();
await AttachmentRootChecks.Run();
await AttachmentMutationChecks.Run();
AttachmentSourceChecks.Run();
AttachmentMetadataChecks.Run();
PngProfileChecks.Run();
ClipboardPngChecks.Run();
await AttachmentPreparationChecks.Run();
await AttachmentLeaseChecks.Run();
await AttachmentFileTransferChecks.Run();
await MarkdownFileTransferChecks.Run();
BoundedFileReaderChecks.Run();
PngDecoderChecks.Run();
DibToPngChecks.Run();
PdfRasterDocumentChecks.Run();
RichDocumentChecks.Run();
MarkdownChecks.Run();
await MarkdownQueueChecks.Run();
await RichStorageChecks.Run();
SearchChecks.Run();
SearchExcerptChecks.Run();
OrganizationChecks.Run();
OrganizationPreflightChecks.Run();
await BatchChecks.Run();
HistoryDiffChecks.Run();
await Schema2Checks.Run();
await BackupChecks.Run();
await PreparedBackupChecks.Run();
await BackupPreviewChecks.Run();
await SelectedBackupChecks.Run();
await WholeMigrationChecks.Run();
OfficeTextExportChecks.Run();
await ExcelImportChecks.Run();
AutomaticBackupPolicyChecks.Run();
TextTransferChecks.Run();
BatchTextTransferChecks.Run();
StartupChecks.Run();StartupRegistrationChecks.Run();
await FilePathLinkChecks.Run();
PdfTextExportChecks.Run();
await DeviceUiChecks.Run();
DeviceSearchStateChecks.Run();
await DeviceSearchActivationChecks.Run();
await LocalIdentityCreationChecks.Run();
VaultChecks.Run();
VaultFailureChecks.Run();
await CoordinatorChecks.Run();

await SelectedPngChecks.Run();

TrashLineageChecks.Run();
AutomaticTrashChecks.Run();
UserLinkChecks.Run();
PngMetadataChecks.Run();
StructuredWordChecks.Run();
SpreadsheetScalarChecks.Run();
SpreadsheetScalarImportChecks.Run();

await TrashEncryptionChecks.Run();

await InlineImageChecks.Run();
await Schema10ImageChecks.Run();

await Schema10ImageHiddenChecks.Run();

await InlineImageAdmissionChecks.Run();

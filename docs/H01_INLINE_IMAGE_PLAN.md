# H01 Canonical Image Block Implementation Plan

> **For agentic workers:** Implement task-by-task with the parent's assigned Core, Windows and test owners. Use `superpowers:executing-plans` for inline execution or `superpowers:subagent-driven-development` for already-authorized delegation. Checkboxes record execution; none is complete merely because this plan exists.

**Goal:** Explicitly place an already authenticated PNG attachment at a rich-document block boundary, preserve that placement in encrypted storage/history/backups, and display its pixels inside a read-only document viewer only after a click.

**Architecture:** Rich document v2 introduces attachment-backed image blocks; payload schema 10 gates their reference semantics. Existing document v1 and payload schemas 1–9 retain their interpretation and source bytes. The first v2 viewer owns no editable embedded WPF content and obtains pixels through the existing authenticated lease, managed PNG decoder and application-wide image/OCR admission.

**Tech stack:** Existing .NET 10 Core/WPF, System.Text.Json, immutable attachment records and envelope2; no new dependency, decoder, network source or key format.

**Spec:** `docs/SOURCE_PROMPT.txt` H01, `docs/PRODUCT_SPEC.md` canonical documents and attachment boundaries, `docs/FEATURE_GAP_AUDIT.md` H01, and existing `docs/plans/2026-10-07-structured-editor.md`, `docs/plans/2026-10-07-attachment-storage.md`, `docs/plans/2026-10-07-image-preview.md`.

**Status:** This agent's deliverable is the plan only. Independent preimplementation review by `/root/schema9_independent_review`, reported to the parent on 2026-10-09, is complete as a **read-only design review: no edits, builds or tests were executed**. Its conditions are incorporated below; runtime acceptance remains unverified. Parent assigned `/root/schema9_core` as Core implementer and authorized independent Core tests/implementation to proceed in parallel after reading this plan. The existing Windows gate is pending; native-editor overlap with C10 is reserved for parent coordination after its actual failure/result is understood. There is no global gate or additional user-confirmation requirement blocking independent Core work.

**Review disposition:** The reviewer supports one existing authenticated attachment inserted at a known-v1 block boundary, readonly v2 display and one clicked image. Required conditions are sticky schema10 (including policy setter/hidden root), record-local subset references, unchanged opaque v1, Core v2→v1 capture refusal, same-root selected import with candidate10 before validation, detach refusal, shared admission/late-setter cleanup and explicit export capabilities. Tasks 1–5 assign each condition and its checks; parent performs the postimplementation independent review. No review result is being represented as an execution result.

## Global constraints and fixed decisions

- Synthetic inputs only; preserve all 134 feature IDs and mark H01 partial/user acceptance unverified.
- Existing bounds remain: source JSON 1 MiB, derived text 65,536 UTF-16 units, nodes 1,024, runs 4,096, JSON depth 16/token budget 16,384; attachment object 4 MiB, retained plaintext total 8 MiB, 128 objects, 16 distinct active attachment references; envelope 16 MiB. Image blocks count toward the existing node limit. Repeated blocks may reference one object; no new limit expands existing budgets.
- Only document v2 recognizes `{"type":"image","attachmentId":"<lowercase UUID in D format>","alt":"<label>"}`. Required `alt` is 1–256 well-formed UTF-16 units, trimmed, without control characters. Default insertion label is the authenticated attachment name. No URI/path, bytes, dimensions, decode result or key is stored in this block. Other known block/run forms keep their existing rules.
- Image IDs must be nonempty, canonical UUIDs in the same record's `AttachmentIds`, resolving to immutable authenticated objects in the snapshot's nonempty root with MIME exactly `image/png`. Image references are a subset of record attachment IDs, not an equal set; independent attachments remain valid. MIME authorizes attempting the PNG decoder, not trusting pixels. Known-image references are validated for current notes and each historical revision independently, after strict v2 structure validation. Known-image fields are strictly `type`, `attachmentId`, `alt`; extra fields are invalid rather than opaque reference escape hatches.
- A known image contributes exactly `[이미지: ` + alt + `]` to v2's text projection; existing inter-block `\n` rules apply. V2 is a strictly declared format: unknown nodes, unknown fields at any known level and image extra fields are invalid. V1 alone retains its existing opaque/unsupported behavior and authenticated Text; never calculate a partial text projection or reinterpret a v1 node named image.
- Schema 10 accepts v1 opaque content as inert owned JSON even when it contains old image/attachment-looking fields. Such values never grant object access, determine reference retention, cause decode or enable editing. Actual authority remains record `AttachmentIds`. Existing schema 5–9 opaque-reference rejection stays unchanged. Retain unreferenced encrypted objects under current bounds; introduce no garbage collection or reference inference from opaque JSON.
- H01 insertion uses an **already authenticated PNG attachment in an already anchored root**. Keep the existing schema4→5 root-preparation refusal for legacy opaque image/attachment-looking source or history. Do not create a 4→10/root-preparation bypass to evade that refusal; legacy4 remains readable with its exact source/Text/ciphertext, and the rejected root preparation leaves state unchanged. The schema10 opaque preservation rule above grants no root-activation authority.
- A first explicit insertion upgrades only the edited known v1 document to v2 and creates/preserves the complete preoperation revision. Schema migration alone never rewrites any StyledDocument or Text. Untouched source/historical source hashes and legacy ciphertext are invariant. New edited source may change its hash; its predecessor's original whitespace, escapes and field order remain exact in history.
- V2 with zero remaining images stays v2; payload 10 stays 10. No implicit downgrade after removal, restore, policy changes or restart. Old clients fail closed on payload 10/document v2.
- Manual insertion and manual display are separate user actions. No decode on open, selection, insertion, restore, backup preview or repaint. No automatic external opening, thumbnail files or OCR invocation. Editable images, resize/reorder, inline-run images, image paste/drop into the editor, JPEG/general decoding and Word image embedding are outside this unit.

## Review focus

1. Legacy schema4 histories containing opaque image/attachment-looking values must survive migration unchanged without gaining reference authority (Tasks 1–2).
2. Removing the last image or editing automatic-backup policy must not drop payload 10 and later lose content (Task 2).
3. A valid PNG object referenced only by another note, another root or an old revision must not be displayable from the active record (Tasks 1, 3, 4).
4. Late decode/native setters following selection changes, source edits, lock or conceal must not republish pixels; unsettled work continues to occupy the global slot (Task 4).
5. A newly supported v2 image must not silently disappear from Word export, native capture, clone, selected import or hidden recovery (Tasks 2–5).

## Ownership and interfaces

The **Core owner** changes storage/editing/transfer production files. The **Windows owner** changes WPF production files. The **test owner** changes Core/Windows check files and runner dispatch only. Source owners pause together for parent builds; avoid concurrent shared-file edits. The independent reviewer owns no implementation file.

Cross-owner interfaces below are finalized by the parent and Core owner:

- Public `StoredInlineImage(int BlockIndex, Guid AttachmentId, string Alt)` immutable scalar record; public `RichDocumentCodec.Images(StyledDocument)` returns `ImmutableArray<StoredInlineImage>`. For v1 return empty; for v2 extract only structurally valid top-level known image blocks. It grants no read authority.
- `RichDocumentImageEdit.Insert(StyledDocument source, int blockBoundary, Guid attachmentId, string alt)` and `Remove(StyledDocument source, int blockIndex)` return a new `StyledDocument`; this first unit's insertion accepts completely known **v1** source only, removal requires a completely known v2 source and an image at that index. Boundary is 0..top-level node count. Existing v2 remains readonly; inserting another block into it is a later unit.
- Internal `EditingWorkspace.InsertInlineImage(NoteDraft note, Guid id, int boundary, string alt)` and `RemoveInlineImage(NoteDraft note, int blockIndex)` implement complete candidate transactions.
- Public `Task<bool> SaveCoordinator.InsertInlineImageAsync(NoteDraft note, Guid id, int boundary, long expectedEditVersion)` and public `void RemoveInlineImage(NoteDraft note, int blockIndex, long expectedEditVersion)` own session/source/version preflight and atomic publication. No caller-supplied root, object bytes, MIME or label overrides. Use authenticated object name for alt. Insertion requires a completed root-preparation task, an already anchored root and an idle save coordinator, and completes its canonical transaction synchronously through `Task.FromResult`. Do not await root preparation inside insertion: an unanchored previous recovery candidate could otherwise resume after a UI selection change. Separate explicit root preparation must finish first; a clean `SaveAsync` alone need not anchor such a candidate. No legacy root-preparation bypass.
- `RichImageDocumentView(SaveCoordinator session, NoteDraft note, Func<bool> current, Action<string> notice)` is disposable/read-only. Its `DisplayImageAsync(int blockIndex)` and `RemoveImage(int blockIndex)` revalidate the exact current v2 block. Removal is an explicit canonical transaction, not native text deletion.
- Generalize admission's displayed host from `AttachmentPanel` to an internal `IImageDisplayHost` exposing `InvalidateImageDisplay()`; both panel and viewer implement it. Keep one slot/one displayed host across preview, document image and OCR. Preserve existing dispatcher and settlement rules.

## Task 1 — Versioned codec and record-local reference contract

**Core files:** modify `src/MemoApp.Core/Documents/StyledDocument.cs`, `src/MemoApp.Core/Storage/AttachmentValidation.cs`, `src/MemoApp.Core/Storage/SnapshotValidation.cs`; create `src/MemoApp.Core/Documents/RichDocumentImageEdit.cs`.

**Test files:** create `tests/MemoApp.ContractChecks/RichImageDocumentChecks.cs`; extend `AttachmentContractChecks.cs`; add a focused `--h01-image-only` runner route in `tests/MemoApp.ContractChecks/Program.cs`.

- [ ] Write RED cases for v2 image text between two styled paragraphs; first/last/repeated image blocks; exact node/run budgets; invalid UUID/empty ID/URI/path/bytes/extra known-image fields/duplicate JSON fields/oversized or control-character alt; forbidden image inside runs/list items/table cells; v2 unknown nodes/fields rejected without partial Text; historical and current dangling/nonlocal/non-PNG references.
- [ ] Preserve existing schema5 current/history/nested opaque-reference rejection tests and legacy4 opaque reference root-preparation rejection. Add schema10 v1 opaque fixture with whitespace/Unicode escapes/numeric lexemes/attachment-looking fields and independent authenticated Text; assert exact source hash/Text and zero extracted authorized image refs. A direct validation fixture does not authorize upgrading legacy4 by bypassing root preparation.
- [ ] Run the focused Core command and record actual RED assertions before implementing. Implement version dispatch and document validation with the record's attachment IDs and immutable object lookup; do not make `Inspect(v1)` recognize images.
- [ ] Implement source edits using existing block raw JSON for untouched nodes; revalidate the new document and bounds. Reject opaque source editing. Verify the predecessor's complete raw source is retained by the transaction in Task 3.
- [ ] Run focused Core checks GREEN and existing rich/attachment contract checks. Review extraction and snapshot validation together; do not publish production UI yet.

## Task 2 — Sticky payload 10, migration and transfer preservation

**Core files:** modify `src/MemoApp.Core/Storage/SnapshotValidation.cs`, `SnapshotSerialization.cs`, `AttachmentEnvelope.cs`, `EncryptedVault.cs`; `src/MemoApp.Core/Editing/EditingWorkspace.cs`, `EditingWorkspace.ImportBackup.cs`, `SaveCoordinator.cs`; existing backup/hidden preparation files discovered through those call sites.

**Test files:** create `tests/MemoApp.ContractChecks/Schema10ImageChecks.cs`, `Schema10ImageHiddenChecks.cs`; extend `AttachmentEnvelopeChecks.cs`, `WholeMigrationChecks.cs`, `SelectedBackupChecks.cs`, `SelectedBackupFailureChecks.cs`, `AutomaticBackupPolicyChecks.cs` as relevant; wire them into the focused route and full runner.

- [ ] Write RED for envelope2 schema10 root pairing, decrypt/restart, current/previous selection, frozen actual schema1–9 byte fixtures, payload9/v2 rejection, future payload11 rejection, migration write/flush/replace faults and exact previous ciphertext. Hash every original v1 SourceJson and assert no migration-only source/Text/history mutation. Do not replace frozen legacy fixtures with current serialization masquerading as old bytes.
- [ ] Write RED for schema10 remaining sticky after last-image removal, restoring a v1 revision, duplicate/clone, selected backup import and clear/reload. Import of v2 raises the candidate schema to at least 10 before validation; cross-root/object identity conflict remains rejected and leaves all current state unchanged.
- [ ] Write RED for schema10 → automatic policy enable/edit/disable → Capture/Prepare/save/restart remaining schema10. Fix the existing `SetAutomaticBackupPolicy` literal `SchemaVersion=9` to `Math.Max(9, candidate.SchemaVersion)`; never reconstruct a lower-version candidate.
- [ ] Write RED for hidden lock/resume/save-failure paths containing valid v2 and opaque v1 history; specifically prepare10 failure→hidden10 resume must preserve document bytes, attachment objects, automatic policy and discarded witnesses. Prepared/reopened10 downgrade attempts must fail. Invalid hidden image refs must fail before publication. Include manual/automatic/locked committed-copy backups and restoration of document+Text+IDs+objects under existing budget/fault rules.
- [ ] Run RED, then add sticky image-version state to workspace initialization, Capture, AcceptPrepared and Clear, and prepared/downgrade state to vault. Audit **every** explicit schema1–9 or schema5–9 whitelist and pairing in envelope, Open, Prepare, `InitializeAttachmentRoot`, `ValidateHiddenRoot` and backup paths. Extend existing envelope2 key ownership/nonce reservation rules to 10 without new cryptography or reused object-encryption authority.
- [ ] Ensure all schema10 records, including historical known v2 images, are checked by both normal and hidden candidate validation. Preserve complete v1 opaque data as inert; never use it as a source for leases or automatic reference repair.
- [ ] Run focused Core GREEN plus migration/root/backup suites; independently review security invariants before enabling insertion.

## Task 3 — Atomic image insertion/removal and export refusal

**Core files:** create `src/MemoApp.Core/Editing/EditingWorkspace.RichImages.cs`, `SaveCoordinator.RichImages.cs`; modify existing attachment-detach logic in `EditingWorkspace.cs`, and `src/MemoApp.Core/Transfer/StructuredWordExport.cs`.

**Test files:** extend `RichImageDocumentChecks.cs`, `AttachmentMutationChecks.cs`, `StructuredWordChecks.cs`; use existing preparation/lease failure fixtures.

- [ ] Write RED for inserting an existing active `image/png` attachment at boundary 0/middle/end of a supported known-v1 rich note; assert original paragraphs/run marks, original preoperation SourceJson hash, retained predecessor Text/IDs and independent new revision. Explicit additions to a known v2 document remain block transactions. Reject stale version/lock/unanchored root/pending preparation/nonactive object/non-PNG/opaque source before any publication; insertion must not activate a previously unanchored legacy root or wait for save work.
- [ ] Write RED for explicit block removal, last block removal, repeated references, retained attachment objects and history; reject detached ID while any active known image still references it. Removing a block keeps its attachment connected; removal and later detach are separate explicit actions. No permanent deletion occurs.
- [ ] Write RED for Word capture of a v2 document containing images: refuse the complete operation before producing a file until embedding is supported. Text-only exports may use the declared image marker and existing loss rules; encrypted transfers always preserve the complete source/attachments. No switch may skip a supported image silently.
- [ ] Write RED for `EditingWorkspace.SetRichDocument` attempting v2→v1 native replacement, even if the replacement's text superficially matches. Reject independently of UI readonly state. Existing explicit confirmed mode conversion remains a separate history-preserving operation, not a bypass for native capture. Add v2 paste/Undo/IME refusal checks in Task 4.
- [ ] Run RED, implement the fixed interfaces using complete candidate validation and existing revision/preparation patterns, and run focused Core GREEN. Root/schema preparation and lease authority are never substituted with a UUID lookup alone.

## Task 4 — Read-only positioned viewer and explicit shared rendering

**Windows files:** create `src/MemoApp.Windows/RichImageDocumentView.cs`; modify `ImagePreviewBackend.cs`, `AttachmentPanel.cs`, `AttachmentPanel.Ocr.cs` for the common displayed-host boundary; modify `StructuredNoteEditor.cs` only to make v2/native-capture capabilities explicit.

**Test files:** create `tests/MemoApp.WindowsChecks/RichImageDocumentChecks.cs`; reuse/extend synthetic backend patterns from `ImagePreviewChecks.cs`, `ImagePreviewRaceChecks.cs` and `OcrProductChecks.cs`; wire focused `--h01-image-only` WPF groups in `Program.cs`.

- [ ] Write actual-WPF RED fixtures for readonly ordered paragraph/checklist/table/image blocks; opening/insertion/selection/restore performs zero decode; only the selected image button starts decode. Native rich capture and formatting cannot save a v2 document as v1 or drop image nodes. No `InlineUIContainer`/`BlockUIContainer` is admitted to the editable rich capture path.
- [ ] Write RED for document-image versus attachment-preview versus OCR global busy refusal, including unsettled OCR cleanup retaining admission. A successful new image display clears the previous host through the shared interface; no independent slot/cache is introduced.
- [ ] Write RED for slow decode with note selection change, attachment selection change, content/edit version change, schema replacement, block removal, preview/session epoch change, conceal/lock, close and dispatcher shutdown. Retain no publication authority in worker/backend; pending lease/raster cleanup follows existing revocation rules.
- [ ] Write RED for reentrant `BitmapSource` creation and Image.Source/Visibility setters that revoke authority or throw. Guards compare session identity, active note identity, exact Document/version, block index+ID, display generation, cancellation and attachment-preview epoch before and after every native publication boundary. Cleanup detaches Source even if one setter throws.
- [ ] Implement the viewer without an editable native document or image-related native Undo. Reuse owned raster/backend methods and managed PNG profile. Display one bounded image at a time; no additional decoder, automatic paint decode, on-disk thumbnail or external URI constructor.
- [ ] Run focused WPF GREEN on Windows; Linux compilation is not WPF runtime evidence. Preserve existing attachment preview/OCR/rich native refusal groups.

## Task 5 — Main/sticky insertion integration and final gate

**Windows files:** modify `AttachmentPanel.cs`, `MainWindow.xaml.cs`, `MainWindow.xaml`, `StickyNoteWindow.xaml.cs`, `StickyNoteWindow.xaml`, and existing rich-host lifecycle helpers where they currently instantiate `StructuredNoteEditor`.

**Test files:** extend `RichImageDocumentChecks.cs`, `AttachmentUiChecks.cs`, `InstalledPackageChecks.cs` only for this unit's product route; update runner groups. Feature evidence belongs to the parent's normal postverification documentation pass.

- [ ] Write actual-product RED for Main and Sticky: selected authenticated attachment → explicit "문서에 이미지 추가" → append after the selected top-level block. An empty selection inside a top-level paragraph or checklist/table container maps to the boundary after that block; reject nonempty/multi-block selections. Explicit insertion requires an editable known v1 document; v1 opaque and existing v2 insertion attempts are refused. There is no invented text caret inside the readonly viewer.
- [ ] Write RED for canceled/stale insertion, Main/Sticky shared canonical state, single→multi selection, modal preparation lock/source changes, fold/close/conceal and a disposed viewer receiving late callbacks. Bind authority to both current note and current host identity.
- [ ] Verify save→lock→restart and manual/automatic/selected backups preserve v2 placement and attachment bytes while pixels remain undisplayed until another explicit click. Test active removal returning to readonly v2 and history restore retaining exact v1 source.
- [ ] Run focused Core and Windows GREEN; then parent runs `python3 tools/verify_preparation.py`, full Core checks, cross-build and one actual full Windows gate for the final source. Run sensitivity/artifact/diff checks. Re-run only a changed source or unresolved failure, and record Linux/build/runtime/physical-user evidence separately.
- [ ] Update H01 evidence as partial canonical PNG block placement/manual display. Keep editable inline images, general image formats, clipboard/drop insertion, Word image export and user acceptance explicitly unfinished. Integrate only after independent review findings and runtime failures are resolved; no main push/public release is implied.

## Verification commands and handoff

The focused routes below are deliverables of Tasks 1 and 4, not commands currently assumed to exist:

```text
dotnet run --project tests/MemoApp.ContractChecks -c Release -- --h01-image-only
dotnet run --project tests/MemoApp.WindowsChecks -c Release -- --h01-image-only
python3 tools/verify_preparation.py
dotnet run --project tests/MemoApp.ContractChecks -c Release
dotnet build tests/MemoApp.WindowsChecks/MemoApp.WindowsChecks.csproj -c Release
dotnet run --project tests/MemoApp.WindowsChecks -c Release
git diff --check
```

The Windows run commands require Windows. Parent owns combined build/runtime scheduling and integration. Independent Core work already has parent authorization and proceeds from this saved plan and review disposition. Coordinate native-editor changes with the pending C10 gate; final integration still requires resolving runtime failures and review findings. This file is the only deliverable of this planning agent; no user-confirmation step is introduced.

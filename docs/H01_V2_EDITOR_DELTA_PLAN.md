# H01 Canonical v2 Text Editor Delta Plan

> Planning deliverable only. Independent PRE review is required before implementation. Use the parent's assigned owners and coordinated builds; this document does not authorize product edits, commits or publication. Apply `superpowers:executing-plans` when implementation is assigned.

**Goal:** Edit supported rich text, styles, lists, checklists and tables around existing canonical image blocks without losing or granting new image authority.

**Architecture:** Reuse `StructuredNoteEditor`'s bounded FlowDocument renderer/serializer and transaction/event guards. Represent image positions with closed, owner-registered inert text placeholders; capture them as their original canonical image blocks only after full structural and source-authority validation. Pixel display/removal remains an explicit, separate existing image-view action.

**Tech stack:** Existing .NET 10 Core/WPF/System.Text.Json; no dependency, XAML/RTF importer, decoder, network service or storage schema change.

**Spec:** `docs/SOURCE_PROMPT.txt` C01–C15/H01/H08, `docs/PRODUCT_SPEC.md`, `docs/H01_INLINE_IMAGE_PLAN.md`. Existing source is authoritative for currently implemented limits.

## Current prerequisite source evidence

The test-only `RichImageEditorPrerequisiteChecks.cs` and runner are implemented after independent PRE. Actual cf7 run38022122133 and final35 run38023414844 passed native placeholder/Undo/Redo/manager composition boundaries0/1/3/repeated0,2,3 with exact events/Unicode and Paragraph/Run identity. Manager-driven synthetic composition is separate from physical OS IME acceptance and new product-editor verification. Final35/artifact11659571081 retains the read-only v2 host. The nine-file uncompiled Core/editor WIP was archived while the parent prioritized PDF/OCR gates; after renewed continuation authorization and independent recovery PRE it was restored once on the identical app/test baseline, with archive/ref/patch preserved. Focused/new-native/POST/host verification remains required; restoration is not completion.

## Requirement provenance and feasibility

The original request explicitly includes rich editing, image attachment, undo/redo, and preservation of tables/styles/images through round trips. It does not explicitly prescribe editable document v2, immutable image placeholders, or the interaction proposed here. Treat editable surrounding text as an inferred functional continuation of those requirements, not a new claim that every image editing feature was requested or completed.

The gap is confirmed by source inspection: `MainWindow.SelectEditor` and `StickyNoteWindow.ConfigureBody` route all schema-2 documents to `RichImageDocumentView`; that control is read-only. `StructuredNoteEditor.PublishProjection` enables only v1, `CaptureDocument` refuses v2, and `EditingWorkspace.SetRichDocument` rejects either current or candidate v2. These are deliberate preservation gates and must remain for callers outside the new closed path.

Feasible as a bounded delta, conditional on actual Windows placeholder/undo/IME behavior passing below. There is no runtime proof in this document. A single editor is preferred over multiple per-segment editors because existing paragraph/list/table commands and shared Main/Sticky behavior can be reused. Generic embedded UI capture is rejected because it would widen native content authority. Segment editors remain a future design alternative only if the native prerequisite fails; do not silently switch architecture during implementation.

## Hard contracts

- Retain current source/node/run/text/depth/token, history, attachment and envelope bounds from the H01 plan; no limit increase, downgrade, garbage collection, attachment alias, new root activation or new payload version. A zero-image v2 stays v2.
- Edit completely supported canonical v2 only. Unknown/opaque documents, unavailable native font/style fidelity, malformed source or failed full round-trip equivalence remain wholly read-only with the exact source preserved. Never serialize a partial document or convert v2 to v1 to enable typing.
- Existing image blocks are immutable in this path: exact attachment ID, exact alt and original image-to-image order/multiplicity remain unchanged. Surrounding text blocks may be inserted, deleted or edited. Moving an image relative to another image, copying an image token, native deletion of an image token or editing its label refuses the whole native candidate. Existing explicit image insertion/removal remains a separate canonical transaction and forces a fresh editor projection.
- Image removal changes document placement only. It does not detach or reclaim the object; predecessor history and independent attachments remain. This delta does not add native image removal/undo, resizing, drag/reorder, inline-run images, JPEG, paste/drop images or automatic display.
- Edited source may be canonically serialized, but the complete predecessor's original SourceJson/Text and attachment references remain in the existing history transaction. Unedited original source and histories are never rewritten by opening the editor. Semantic no-op does not create a revision; supported Undo returning to the projection's original model restores its exact owned SourceJson as existing v1 does.
- Metadata, title, tags, attachment IDs/objects/root and attachment OCR derivations are not edited by this path. OCR data may become stale under existing source/version policy but must not be silently removed, synthesized or relabeled.
- No plaintext image bytes, lease, bitmap or image-decoder worker enters the text editor. Text placeholders do not grant read authority. Existing explicit image display retains global image/OCR admission and its own exact-current checks.

## Proposed closed interfaces (PRE review must finalize before coding)

Core owner adds an internal sealed `RichImageTextEditContext`, with private construction by the coordinator, exact original workspace/note/StyledDocument identity, source EditVersion, session/preview epoch, original immutable image-block descriptors and original record-local immutable object membership. It owns no image bytes. Trusted Dispatcher access is registered once using the established stable-owner-policy pattern, not supplied as a renewable per-edit predicate.

- `SaveCoordinator.CaptureRichImageTextEditContext(NoteDraft note, long expectedEditVersion, long expectedPreviewEpoch)` returns the closed context after validating the current known-v2 document and original current-record image membership. An allocation/constructor failure leaves no issued context.
- `SaveCoordinator.IsRichImageTextEditContextCurrent(RichImageTextEditContext context)` checks issuer identity, original session/workspace/note membership, exact source document/version/epoch and immutable objects on the owner thread.
- `SaveCoordinator.ApplyRichImageTextEdit(RichImageTextEditContext context, StyledDocument candidate)` returns a closed receipt with `NotApplied`, `NoChange` or `AppliedDirty`, plus a newly issued continuation context only for a successfully validated unchanged host/source handoff. The complete candidate is validated before publication; all image descriptors must exactly equal the original sequence. No arbitrary caller image ID or alt grants access. Existing generic `SetRichDocument` still refuses v2.
- `SaveCoordinator.RetireRichImageTextEditContext(context)` is idempotent retirement of exact issued authority; disposed/retired contexts cannot be renewed. Main and Sticky may have distinct contexts; a real edit in one invalidates the other's original source immediately.

Use existing dirty-draft/history semantics, not `ApplyImageDocument` per keystroke if that would mint committed revisions per edit. Publication must have the same staged full-candidate validation as `SetRichDocument`, strengthened with original-source checks and pre-publication read invalidation. A trusted continuation is confined to the exact successful own transaction. Nested owner events/reentry, other metadata edits, save/AcceptPrepared, selection away/back or arbitrary epoch changes never become an automatic refresh of old edit authority. Report `AppliedDirty` truthfully if the mutation occurred but UI publication fails; retain the valid dirty state and rebuild/clear safely.

## Independent PRE condition — publication must be closed

Independent PRE found that existing `NoteDraft.StageEvent` invalidates attachment reads through external callbacks and then installs fields without an intervening source recheck. The new v2 path must not simply reuse that sequence. Before any invalidation, finish all fallible candidate/history/accepted-version/receipt/continuation allocations; consume the exact context own-attempt; after every pre-invalidation callback recheck original membership, document, EditVersion, session/global epoch and trusted owner access; then perform the complete dirty-state install and inert Applied marker without callbacks. Notifications occur afterward. A trusted own continuation permits only the expected single own invalidation and exact post-source transition. Nested metadata/source edits, AcceptPrepared, lock or selection changes forbid continuation. An applied mutation followed by notification failure remains AppliedDirty; never report NotApplied or restore the predecessor over a newer edit. The current product readonly gates remain unchanged until this condition and actual native prerequisite are verified.

## Native placeholder and editor contract

Create `StructuredNoteEditor.Images.cs` as a narrow partial. Each image block projects to an ordinary inert Paragraph containing one owned Run showing its existing image label. No BlockUIContainer, InlineUIContainer, FrameworkElement, URI, XAML, Tag, public attached-property token or native text spelling is an authority source. A private registry maps the exact created Paragraph/Run references to a closed descriptor plus projection generation and context identity.

Capture accepts an image placeholder only when the exact registered paragraph is a top-level member of the current owned FlowDocument; its exact registered Run is its sole inline; text and supported fixed presentation are intact; no child, nested paragraph, clone, additional run or inherited active native content is present; and the registry/context/source remains current before and after traversal. Image blocks are emitted from original raw canonical nodes, never from displayed label text. Each registered image must appear exactly once, in original relative order. A normal paragraph containing the same visible label is ordinary text and grants no image access.

Reuse the existing known text-block serializer for every other block; continue refusing active Hyperlink/native embedded content, unsupported structure/styles and excessive native traversal. Full schema-2 Inspect/equivalence and Core image-sequence validation must both succeed before publishing. Do not waive fidelity because a placeholder exists.

Preview commands and plain-text paste that touch a placeholder refuse without applying a partial replacement. Defensive post-capture validation also refuses direct native mutations, cross-image selection deletion, cut, formatting, list conversion and unexpected WPF normalization. Rebuild from authoritative source after rejection; do not preserve invalid native Undo entries. Text-only selection/typing/formatting and Undo/Redo within supported surrounding blocks retain existing native history. Native Undo that recreates/clones placeholder identity is an explicit prerequisite failure, not permission to trust copied IDs or Tag values.

Preserve existing EventDepth, BeginChange retry, detached projection, IME completion generation, caret generation, source-exact undo and plain-text-only paste guards. Immediately revoke contexts and clear native text/Undo/registry on true host close/lock/conceal/selection-away-back, source replacement, epoch change or failure. Guard every native publish/setter before and after; a late callback must not resurrect text. Source-edit invalidation also immediately clears any displayed image pixels through existing image-view invalidation.

Main/Sticky offer editing and the existing explicit image-view controls in the same selected-note host without duplicating implicit decode. Pixel display remains a click; editing text clears displayed pixels. Image view may rebuild its block positions after successful edits. Existing append-to-v2 image insertion remains unchanged in this delta; no new caret-based image movement is implied.

## Ownership and implementation units

### Task 1 — Closed canonical v2 text transaction (Core owner)

**Create:** `src/MemoApp.Core/Editing/SaveCoordinator.RichImageTextEditing.cs`, `tests/MemoApp.ContractChecks/RichImageTextEditingChecks.cs`.
**Modify narrowly:** `EditingWorkspace.cs` or new `EditingWorkspace.RichImageTextEditing.cs` for the staged dirty transaction; existing lifecycle invalidation entry points only as necessary. Runner registration belongs to parent.

- [ ] Write failing tests for text/style/list/checklist/table edits around first/middle/last/repeated images; exact image raw blocks/IDs/alt/order; zero-image v2; no-op and original-source history/undo restoration.
- [ ] Add whole-candidate refusal for foreign/old-revision/nonlocal/detached objects, cloned/reordered/deleted/changed image descriptors, unsupported source/candidate, every existing limit, stale version, same-version AcceptPrepared, metadata edit, lock/dispose, Main-vs-Sticky stale context and nested prepublication invalidation. Assert exact state/cipher unchanged on refusal.
- [ ] Implement the finalized closed API and transactional continuation receipt; independent security review before Windows integration. Save/reopen/import/history/hidden recovery must preserve edited v2 and unchanged object/OCR data under existing fault policies.

### Task 2 — Registered native placeholders (Windows editor owner)

**Create:** `src/MemoApp.Windows/StructuredNoteEditor.Images.cs`, `tests/MemoApp.WindowsChecks/RichImageTextEditorChecks.cs`.
**Modify:** `StructuredNoteEditor.cs` only at constructor/projection/capture/commit/lifecycle seams; Checklist/Links partials only if command guards require it.

- [ ] Establish native RED against frozen actual baseline. A PE missing-API probe is metadata evidence only; Linux build is not native RED.
- [ ] Run actual detached FlowDocument/Undo/Redo/IME identity prerequisites before enabling v2. Prove text Undo around every image placement retains exact registered placeholders, or leave the feature disabled and report the failed prerequisite.
- [ ] Prove Paragraph/Run clones, spoofed label/Tag/attached values, arbitrary UI containers, direct native document injection, cross-placeholder paste/cut/delete/format/list commands and reordering are whole refusals with exact source/history/cipher preserved. Add legitimate ordinary text equal to image label to prove no false authority.
- [ ] Prove real text/styles/table/checklist edits and Undo/Redo preserve canonical images and surrounding text; original raw source returns exactly on supported Undo. Assert no authenticated read/decode occurs on projection, typing, Undo, selection or redraw.
- [ ] Inject source/selection/lock/close/reentry during actual Document/IsReadOnly/IsUndoEnabled native setters and queued IME/BeginChange refresh; require immediate clear/no late text or pixels and retired exact context.

### Task 3 — Shared Main/Sticky integration (Windows host owner)

**Modify:** `MainWindow.xaml.cs`, `MainWindow.InlineImages.cs`, `StickyNoteWindow.xaml.cs`, `StickyNoteWindow.InlineImages.cs`; `RichImageDocumentView.cs` only to coordinate explicit display invalidation/presentation, without widening decoder authority. Parent owns runner and shared build/publication.

- [ ] Replace the v2 read-only routing with the proven editor plus explicit image-view actions; preserve fallback/refusal behavior and title/attachment controls.
- [ ] Test actual Main and Sticky editing the same note: the other projection becomes inert immediately, trusted own continuation cannot adopt the other's edit, and source remains correct after save/reopen. Test selection away/back, sticky fold/close, lock/unlock, same-version source acceptance, image insert/remove and displayed-image clearing during text changes.
- [ ] Run focused Core checks, preparation verifier and coordinated full builds; execute actual Windows editor regression groups and new native checks. Independent postimplementation review precedes any feature-status update. Retain partial H01 and unverified user acceptance unless evidence supports a narrower truthful change.

## Review focus and evidence limits

The five highest-risk inputs are: WPF Undo rebuilding placeholder identity; selection spanning an image; IME/BeginChange completion after source replacement; concurrent Main/Sticky edits; and nested publication invalidation after the candidate is already dirty. Tasks 1–3 explicitly assign their assertions. No runtime, build, viewer acceptance or secure erasure claim is made here. PRE review must reject any proposal that treats native label/Tag identity, a refreshed source epoch, or copied attachment IDs as sufficient authority.

Only this new document is edited for the feasibility pass. No implementation begins until parent coordinates the closed APIs/owners and independent delta PRE review.

# H09/T12 Fixed Synthetic OCR Corpus Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this bounded test-only unit after independent PRE review. Parent owns runner registration, coordinated builds and publication.

**Goal:** Exercise four declared public-synthetic PNG variants through the existing verified production linked OCR worker, recording accuracy limitations separately from lifecycle/provenance correctness.

**Architecture:** New WindowsChecks partial builds deterministic fixture bytes from the existing public image and validates their pixels independently. Each fixture is encrypted and committed before capturing an authentic attachment lease. The existing detached `LinkedOcrStarter` runs the fixed installed engine/models; tests never construct trusted output or launch an unverified child.

**Tech stack:** Existing .NET10/WPF, existing PNG/zlib primitives and fixed Tesseract5.5.3/Leptonica1.87.0 kor+eng bundle. No package, model, font, asset download or product change.

**Spec:** `SOURCE_PROMPT.txt` H09/T12 and local-processing/security requirements; `PRODUCT_SPEC.md`; `H09_OCR_SEARCH_PLAN.md`; `plans/2026-10-09-local-ocr-windows-integration.md`.

## Scope and current evidence

Current35 native runtime/product/linked-search tests reuse `tests/fixtures/ocr-synthetic-png.base64`, SHA256 `21f8894d43b5c5bfa13db5aaf1503ad726daf4675a01623e5c5778b0a5f89ca8`, 1000×450, and the fixed `OcrExpected` string. Existing decoder tests already cover channel layouts, filters, maximum geometry, downsampling, corruption and actual zeroing. Generic pending races, genuine linked publication, encrypted apply/search/reopen and sampled network observation already passed; do not recreate them as missing behavior.

This delta varies image representation/scale/contrast, not the text corpus. It does not establish broad Korean OCR quality, resource suitability, physical-user acceptance or engine provenance for synthetic-backend tests. New text/font samples are a later independent unit.

## Global constraints

- Own only new `tests/MemoApp.WindowsChecks/OcrSyntheticCorpusChecks.cs` and this plan. Reuse existing private partial helpers without changing their semantics; any necessary helper extension requires explicit parent coordination. No production/backend/grant seam.
- Preserve PNG4MiB, axes1..4096, source pixels4,194,304, depth8 color types0/2/4/6, no interlace, chunks256/structure65,536 and preview1024² bounds. Existing production inspection/strict decoding must accept every generated variant.
- Keep fixed nearest-neighbor/straight-alpha-white transform, exact PPM digest, kor+eng/OEM1/PSM6/OMP_THREAD_LIMIT1, 20-second native deadline, stdout262,144/stderr4096 and UTF16 text65,536 unchanged.
- Four sequential actual inferences only. Models install once through existing verified embedded bundle into a temporary public-model directory. No engine/model substitution, parameter tuning or cloud fallback.
- Read manifest resource `MemoApp.OcrManifest` within its existing16KiB cap. Require expected engine/model hashes and pinned source commits through existing production verification/provenance.
- All input/expected text is public synthetic. Log fixture ID/hash/scalars/accuracy counts/timing only; no raw recognized text, paths, endpoints or secret bytes.
- Assertion timeout is failure, not settlement. Cancel/dispose starter and actually await Completion and Settled in owning finally. Never delete a fixture directory or dispose its owner before actual cleanup.

## Fixed variants and independent pixel oracles

The reference image is decoded independently with WPF `PngBitmapDecoder`, `PreservePixelFormat|IgnoreColorProfile`, `OnLoad`, then straight `Bgra32`. Retain those test-owned reference pixels only for bounded fixture construction/comparison and zero them in finally. Composite alpha onto white using integer `(channel*alpha+255*(255-alpha)+127)/255` before deriving opaque fixtures. Original fixture bytes remain exact and are not re-encoded for the baseline.

| ID | Declared transform | Source / expected preview | Accuracy assertion |
|---|---|---|---|
| original | Exact existing PNG bytes | 1000×450 /1000×450 | Preserve existing whitespace-normalized content equality with `OcrExpected` |
| gray | Opaque luminance `Y=(77*R+150*G+29*B+128)>>8`; PNG color0 | 1000×450 /1000×450 | Report raw and whitespace-normalized errors; no invented universal accuracy threshold |
| twice | Duplicate every composited opaque source pixel into2×2; PNG color2 | 2000×900 /1024×460 | Report errors against the same declared text; pin production nearest mapping independently |
| low-contrast | Gray luminance above, then `160+(Y*80+127)/255`; PNG color0 | 1000×450 /1000×450 | Report quality limitation; do not adjust contrast or oracle after observing output |

Test-only PNG encoder emits only signature/IHDR/one IDAT/IEND, depth8/noninterlaced and filter0 scanlines using existing zlib. Validate generated headers, chunk CRCs, exact decompressed row lengths/filter bytes and full decoded pixel arrays independently before OCR. WPF decode must match the declared per-pixel transformation, not a checksum computed solely by the encoder. Pin transforms with small hand-authored RGB/alpha/gray samples and deliberate altered-pixel/geometry negative checks. Input SHA is recorded for each deterministic generated fixture; compression bytes are not claimed identical across arbitrary future runtimes.

Use existing `IndependentLinkedPpmHash` for the independent WPF nearest/white digest. Dimensions are asserted against the table, not merely compared between production-derived values. Verify source byte/hash preservation after inference.

## Accuracy and measurement contract

- Preserve the existing original baseline `OcrComparable(actual)==OcrComparable(OcrExpected)` oracle. New variants must have genuine successful Completion and successful Settled, valid/nonempty bounded owned UTF8, and all provenance/source assertions; no success-or-refusal alternative.
- Report original and variants' exact-text equality, whitespace counts, raw Unicode-scalar Levenshtein numerator/expected-scalar denominator, and a separately labeled whitespace-removed error count. Raw metric performs no case folding, punctuation deletion, NFC normalization or newline repair. Expected text is always the fixed `OcrExpected`; it is never learned from recognized output.
- Metric correctness gets hand-authored identical/insertion/deletion/substitution/emoji/space/newline vectors, including intentional content mutation detection. Two-row bounded edit-distance calculation; expected text is the fixed small string and actual output keeps existing production bounds.
- Variant character errors are diagnostic measurements, not an accuracy PASS claim. A missing/failed result is still a test failure; do not suppress it as a quality statistic. The group PASS means the declared contract executed, while only the original baseline has an asserted recognition-content match.
- Stopwatch elapsed time includes authenticated input capture, fixed worker startup/verification, recognition and actual settlement; label that boundary explicitly. Record per-case elapsed time, no fabricated realtime guarantee.
- This first unit does **not** add a resource-process observer: log CPU/native peak working set as `not-measured`. Existing elapsed measurement is not falsely described as absent. Optional future CPU/RSS measurement must reuse bounded PID+creation/verified-executable identity, retain actual handles through exit, explicitly report missing coverage, and needs separate narrow review; no unverified direct ProcessStart.

## Review focus

1. Encoder/oracle agreement alone can hide a wrong gray/alpha/scale transform: independent WPF full-pixel checks and scalar negatives are mandatory.
2. Changing expected text or normalizing errors away would conceal poor OCR: only the original existing content gate is mandatory; raw and normalized variant metrics remain separate and honest.
3. Synthetic stamp is not an issued publication grant: use genuine source lease and verified runtime, but never apply the detached result or claim fresh publication authority.
4. Completion may race cleanup or an assertion may throw: attempt-all actual result/input/starter/task/owner cleanup before directory removal, with secret and observed-buffer zeroing even on failure.
5. Dirty Capture provisional GUIDs are unstable: fixtures are committed clean before baseline; exact whole snapshot, edit version/time/dirty state and ciphertext must remain unchanged without AcceptPrepared or hidden save during inference.

## Task1: fixed transforms and metrics

**Files:** New `OcrSyntheticCorpusChecks.cs` only.

**Interfaces:** Private `static Task OcrSyntheticCorpusRun()` in existing partial `Program`; private bounded transform/PNG/metric helpers; reuse `OcrExpected`, `OcrComparable`, `IndependentLinkedPpmHash`, `Field<T>` and `Require`.

- [x] Parent-coordinated immutable previous WindowsChecks PE missing-entry RED (metadata only, not native behavior); no author project build before source pause.
- [x] Add hand-authored transform and metric vectors with deliberate pixel/text negative checks before native cases.
- [x] Construct the four exact variants, independently inspect/full-pixel validate them, and bound every test-owned allocation before creating it. Zero scanline/compression/pixel scratch in finally, including encoder/validation exceptions.

## Task2: verified runtime and invariant cases

- [x] Read bounded assembly manifest, construct existing `WindowsOcrBundle`, install/check the fixed models once and create one fresh temporary encrypted owner with four committed attachments.
- [x] For each variant, capture actual source descriptor and authentic read lease through `AttachmentOcrInput.Capture`, recording existing default-null test allocation observations. Stamp deliberately synthetic and explicitly labeled; no coordinator grant/apply test claim.
- [x] Preallocate existing `LinkedOcrStarter`; fixed Start only, then await actual Completion and Settled. Check exact source/stamp/dimensions/PPM/TextSHA, pinned engine/model commits/hashes and nonempty strict UTF8.
- [x] Consume result once, inspect actual UTF8 zeroing and all captured source/decode buffers after actual settlement. Compare original note/version/time/clean state/full snapshot/all objects/history and current ciphertext to clean baseline after every case.
- [x] Emit bounded fixture/metric/timing summary with CPU/RSS `not-measured`; no raw text or paths. Preserve original content gate regardless of variant measurements.
- [x] Ensure owning finally cancels/disposes and attempts Completion drain, late-result dispose, Settled drain, input dispose, key-release lock and owner disposal independently; always zero secret/source/scratch/cipher buffers. Retain directory on unconfirmed settlement/disposal failure.
- [x] Source-pause and request independent POST. Parent owns Program focused/full registration and coordinated crossbuild/native gate. Crossbuild never counts as actual inference; no native corpus PASS until all four actual cases run.

No user decision is needed for these synthetic tests. Parent/reviewer PRE is the required gate before test code; code and publication remain within the specified ownership boundary.

## Execution boundary (2026-10-10)

Independent PRE/POST found no remaining Critical/Important findings. Previous1bb WindowsChecks PE lacked the new entry (actual exit1, metadata RED only). New source and runner registration compile with0warnings/0errors. Four native inferences remain pending; checked implementation steps do not claim native completion or broad OCR quality.

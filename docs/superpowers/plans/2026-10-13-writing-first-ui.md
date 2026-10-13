# Writing-first Windows UI Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement inline; one independent branch review after verification.

**Goal:** Preserve existing functions while making writing the main Windows flow.
**Architecture:** Reorganize WPF controls and existing handlers. Keep coordinator, encryption, schema11/MEMOV002 and Android unchanged. Sticky windows observe existing save status and detach on close.
**Tech Stack:** WPF / .NET 10.0.401, Python source checks, existing native/core harnesses.
**Spec:** Approved delegated instructions; libfile_9580eba2293481918e381d53e25d6689.

## Global Constraints
- Base 8a3400dabd1b306fce926c56ab51b32272d16e6f; isolated ui/writing-first-20261013 at /workspace/memo-ui.
- No remote push, PR, deployment, user PC or real data.
- Library image materialization failed: supported helper download failed; read-only HTTP diagnosis: tunnel 403 Forbidden. No local image exists, no visual comparison claimed, no permission bypass.
- User already approved design, implementation, verification and artifacts; proceed without another approval cycle.
- Color/transparency/background enhancement is follow-up only.

## Feature → destination mapping
| Existing function | New entry | Handler / invariant |
|---|---|---|
| Create / open sticky / manager-only writing | Primary new sticky; Memo menu new manager note; editor open sticky | Existing CreateNote and OpenSticky, same NoteDraft; focus body |
| Save / lock / failure | Primary Save/Lock; manager Notice; sticky footer SaveState and action message | Existing SaveCoordinator.Changed/Status; close only hides window |
| Whole migration, backup, preview, preserve/compare branches | Backup/recovery named menu | Original handlers and IsEnabled gates |
| TXT/Markdown/Excel import; TXT/PDF/Excel/Word export | Import/export named menus | Original confirmation, file picker and authority guards |
| Duplicate, trash, restore, history, batch/order | Memo menu; textual delete/restore; list/batch controls | Original selection guards; no removal |
| Folder/filter/tag/date/sort, saved/recent search | Three-column manager: classification first; optional search details | Original controls/names/handlers |
| Auto backup/trash | Collapsed sidebar backup settings | Original consent/capacity warnings; corrected purge explanation |
| Attachment, image/path links, OCR | Attachment/OCR expander on both editors | Original AttachmentPanel instance, lifetime and revocation |
| Rich formatting / modes | Collapsible formatting tools; metadata/mode expander | Original editor toolbar/state and ConvertMode confirmation |
| Color, dark/font/scale, transparency | Metadata; View menu; sticky More | Existing bindings/storage preserved |
| Topmost / position lock / list pin / fold | Sticky topmost icon, More position/fold; manager list pin | Distinct labels and native controls |
| Unlock/create/recovery candidates | Recovery-code first; separate create and repair expanders | Original keys, candidate preservation and secret purge |
| Tray/startup/calendar/clock/arrange/show | View/File menus | Original handlers |

## Review Focus
- Locked/failed save must remain visible; sticky observers must detach and not resurrect text.
- All moved commands retain handlers and selection/lock enable gates.
- Collapsed attachment/format controls remain available via keyboard.
- New-note failure must not open an unrelated selected note; manager-only path remains.
- Menu control type changes preserve native IsEnabled reentrancy test coverage.

### Task 1: Screen structure and existing action preservation
Files: MainWindow.xaml; StickyNoteWindow.xaml; StructuredNoteEditor.cs; tests/test_writing_ui.py.
- [ ] Add source navigation tests that preserve every original Click handler and named control; assert collapsed tools and code-first unlock.
- [ ] Run focused tests RED; reorganize controls with accessible icon names/tooltips and native menu/check controls.
- [ ] Run focused tests GREEN; build WindowsChecks after updating moved-control test types without relaxing assertions.

### Task 2: New-note entry and sticky save feedback
Files: MainWindow.xaml.cs; StickyNoteWindow.xaml.cs; tests/MemoApp.WindowsChecks/WritingUiChecks.cs; Program.cs.
- [ ] Add native synthetic checks for manager-only/new-sticky focus, shared draft, save-state transitions, detach-on-close, closed-window note preservation, tool expansion and lock cleanup.
- [ ] Implement existing command routing and coordinator observation; no persistence/security changes.
- [ ] Compile native checks; native runtime is unavailable on Linux, mark native RED/GREEN unexecuted.

### Task 3: Regression, review and delivery
- [ ] Run final verify_preparation, all Python, full Core, cleanup-fault Core and solution cross-build once after changes.
- [ ] Request one independent review; fix important findings with focused checks as necessary.
- [ ] Cross-publish win-x64 self-contained; record OCR bundle absence if MSVC Windows build unavailable.
- [ ] Commit, produce source bundle/patch and trial archive with SHA256; report GUI/IME/DPI/multi-window/restart/PDF/OCR/backup runtime gaps.
- [ ] Actual WPF screenshots unavailable; provide clearly labeled source-layout schematic if possible, never substitute it for runtime evidence.

## Execution ledger
Baseline Python: 77 PASS; source/134 ID invariant PASS. Core baseline pending. Image blocked as above.

Task 1: source navigation5 RED→GREEN, preservation all Click/names PASS, native crossbuild PASS.
Task 2: implemented shared command/status routing and native synthetic test group; native behavior RED/GREEN unavailable on Linux, compile only.
Task 3: Core88 + cleanup-fault PASS; reviewed Python85/skip0; solution crossbuild0warnings/errors; independent review1 found4Important1Minor, all source fixes made, source focused8 GREEN; native fault/layout/focus regression remains unexecuted.
Ruling: Approved user execution instruction supersedes another design/plan approval cycle; no remote integration performed. Cost if wrong: UI alignment needs correction after PNG access.
Ruling: Preserve branch and deliver unverified UI candidate with absent OCR engine, not a full replacement; Windows runtime and packaging gate remains open. Cost if wrong: treating the candidate as ready would lose OCR execution.
Final review: independent source review by gpt-6.1-sol/high; no second review. No deferred minor; preview descriptor correction preserves existing check target.

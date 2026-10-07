# Safe Markdown Implementation Plan

**Goal:** Continue C15 with exact raw Markdown storage and a bounded inert preview without external resources or source writeback.

**Architecture:** Payload schema4 already stores exact Markdown Text and immutable mode/history. Add one reviewed parser dependency to Core, with exact-version allowlist, real NuGet lockfiles and locked restore. Core projects a fixed safe AST to immutable preview records; a later Windows read-only view uses only those records and epoch/version guards. Background work is single-flight with at most one pending source; cancel drops results but cannot terminate an executing synchronous parser.

**Tech Stack:** .NET10.0.401; Markdig exact1.4.0 net10.0 zero transitive dependencies, BSD-2-Clause.

**Spec:** docs/PRODUCT_SPEC.md; docs/plans/2026-10-07-structured-editor.md; original134 IDs and C15.

## Global Constraints
- Source27,417bytes/SHA25629655ee9235ebcc3e056551e1aeaf3af26781f1b43a93b78cd585e285c48957d; exact134 IDs/names/M06 preserved
- Synthetic data only; no account, paid service, public release, new authentication or permissions
- Raw source≤65536 UTF16/well-formed Unicode; bounded preparse delimiter count8192 and nesting32; parserMaximumNestingDepth32; AST≤8192/depth32/output≤65536/blocks1024
- DisableHtml; fixed pipeline, no self/media/configuration pipelines; no HTML/XAML/RTF parsing, external image/URL/shell/dynamic URL execution
- Markdig archive1279982bytes/SHA5128EkBFFlLoQCRS+LKLGdEUN6L57BGkQN2tiR2/zzrd+N2kVFANJzq24TKSeH7lE+ucHcA+OxhlNNmsJAZFJeTqg==; correct recorded hash is rechecked from actual archive before restore
- Lock contentHash lLhaiI/mNDTNzgLqnmknhf4LjgTqOAyjx5+GNz0X3AxYMxUhmFppF2WVMdER+SDqwhE5wUF1pzs/cuLwFxs7xA==; signed archive hash and content hash are distinct
- RestoreLockedMode true for ordinary builds/restores/runs/publish; dependency updates are explicit reviewed operations
- Existing development branch remains development/stage-1 and draft PR1; main merge, public release, permissions and real personal data are excluded

## Review Focus
- Unknown/direct/transitive/ranged dependencies cannot enter the project or stale lockfiles silently
- HTML/images/unsafe protocols remain inert labels with raw source preserved
- Deep delimiters/large AST fail preview safely rather than consuming unbounded work
- Preview conversion does not modify raw Text, mode, history, timestamps or edit version
- Late background results cannot reappear after lock/selection/source change

### Task1: Dependency policy
**Files:** tools/verify_preparation.py; tests/test_dependencies.py; docs/DEPENDENCY_ALLOWLIST.json; Directory.Build.props; project packages.lock.json; docs/licenses/Markdig1.4.0.txt; src/MemoApp.Core/MemoApp.Core.csproj; src/MemoApp.Windows/MemoApp.Windows.csproj
**Interfaces:** verify_dependencies(root:Path)->None; exactly one Markdig [1.4.0] Core reference and known locked contentHash, zero other package references/transitive runtime packages
- [x] Add failing Python policy tests for unknown/ranged refs, lock mismatch/unlisted transitive, disabled locked restore and missing BSD notice
- [x] Observe red missing policy API then implement narrow allowlist/graph checks
- [x] Download full BSD notice from official fixed source commit; verify actual nupkg signature/hash (already independently reviewed); restore initially with explicitly disabled locked mode only to generate real locks
- [x] Check all lockfiles against allowlist; enforce default locked restore and notice copy; run full Python/source/build checks

### Task2: Bounded parser/projector
**Files:** src/MemoApp.Core/Documents/SafeMarkdown.cs; tests/MemoApp.ContractChecks/MarkdownChecks.cs; tests/MemoApp.ContractChecks/Program.cs
**Interfaces:** SafeMarkdown.Preview(string source)->MarkdownPreview; immutable MarkdownPreview(bool Complete,string Message,IReadOnlyList<MarkdownBlock> Blocks); immutable text/style spans, no active resource handles
- [x] Write failing heading/list/code/strong/Unicode/HTML/link/image/raw-fidelity/budget tests and observe unimplemented Preview failure
- [x] Implement preparse budgets, fixed DisableHtml pipeline, bounded AST traversal and inert projection
- [x] Run actual parser tests and full Core suite; independently review new dependency/AST source diff

### Task3: Windows read-only preview
**Files:** src/MemoApp.Windows/MarkdownNotePreview.cs; MainWindow.xaml(.cs); StickyNoteWindow.xaml.cs; tests/MemoApp.WindowsChecks/Program.cs
**Interfaces:** readonly preview control with same-session/epoch/note/source/EditVersion checks; clear source/output/tasks on revoke; never writes into NoteDraft.Text
- [x] Add Windows red-first raw-source/two-view/late-lock/selection-change/large-source tests once approved CI upload resumes
- [x] Implement debounce/background parsing/read-only safe WPF spans and clear behavior
- [ ] Run full actual Windows suite; keep physical IME/OS-lock/ACL/user acceptance separate

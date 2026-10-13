# Windows UI validation checkpoint — 2026-10-13 UTC

**In progress. This source checkpoint is not a completed release or a verified replacement package.**

The writing-first candidate starts at `d2cbed190d6a272bbfb296928d02f261215d23bc`, based on recovery commit `8a3400dabd1b306fce926c56ab51b32272d16e6f`. Only this Windows checkout writes the new validation branch. Main and the recovery baseline are preserved.

## Scope

- An explicit standalone `--synthetic-test` selects executable-side `SyntheticTest/Data` and `SyntheticTest/UiIdentity`. It disables Windows startup-registration access. Ordinary and portable paths retain their existing behavior. Mixed test/portable/tray/install switches are refused.
- Existing storage, cryptography, schema11/MEMOV002, Android and feature IDs are unchanged. The startup-root selection is the only Core product delta.
- Test-only selection permits existing Core and WPF cases to run separately. Filters retain the original assertions and report their actual selected scope. Python child tools use the selected interpreter rather than a platform-specific alias.
- The actual product `App.OnStartup` is also exercised inside the WPF project runner with the real test process argument. This is distinct from installed/self-contained-EXE validation.
- [Adopted concept](design/writing-first-concept.png) was directly viewed. It is a supplied design image, not a live app capture. SHA256: `c48b4afb3e124650fac0258c06fe05ede9c3f498acf60030a610365e184f4c4c`.

## Fresh evidence from the approved standard-user Windows environment

| Check | Actual result and limit |
| --- | --- |
| Fixed official toolchain | SDK10.0.401 and Core/Desktop runtime10.0.12; archive length/SHA512 verified. Isolated CPython3.12.10 x64 and pinned pypdfium2 5.3.0/PDFium145.0.7616.0 prepared. Python digest matched official published Sigstore metadata; independent signature verification was not performed. |
| Native OCR | Project-pinned Tesseract5.5.3/Leptonica1.87.0/kor+eng models rebuilt with existing MSVC. Two actual canonical-P6 memory targets each52/52 PASS; source/model/decoder/hash/license/PE provenance verified. |
| Windows build | OCR manifest embedded in the product assembly; solution builds exit0, zero warnings/errors. No system installation, global PATH, ACL or firewall changes. |
| Startup-root contract | Fixed-SDK focused contract PASS. Explicit test roots, ordinary-path preservation, invalid combinations and ambiguous/non-directory roots checked. |
| Product startup | Actual product App startup inside its WPF runner PASS: test vault/identity paths, locked UI, disabled startup registration, no vault creation. Separate installed-EXE startup remains pending. |
| Writing-first WPF group | Actual Windows group PASS, including new-note body focus, one new sticky, optional tools, folded save footer, saving/saved/failure states, close preservation, format keyboard focus and lock clearing. Physical IME is not established. |
| WPF visual artifacts | Loaded synthetic Window visuals rendered at current192DPI with the actual Window background. Product assembly hashes and PNG metadata bind these local artifacts. They exclude OS chrome, the desktop and physical input. First client-only capture had transparent-background/cropping defects; corrected Window-origin captures replace it as visual evidence. |
| Core individual checks |49 existing filters:44 PASS,5 failed during symbolic-link fixture creation. Six newly selectable existing vault/failure/coordinator/backup/search/organization cases PASS. This is not a full Core pass. |
| Python suite |87 tests:83 PASS,4 errors caused by unavailable symbolic-link setup privilege. The former missing-viewer and interpreter-alias errors were resolved. No skips or weakened assertions were added. |
| Source checks | Original27417-byte prompt/SHA256,134 exact IDs/names and M06 normalization PASS; tracked heuristic safety scan and diff checks PASS. These are not a security audit. |
| Independent review | Bounded startup-isolation source review found no Critical/Important issue. A Minor read-counter assertion gap was corrected. Synthetic-only linked-root case coverage remains pending. |

The original full Core attempt stopped at an unavailable symbolic-link fixture after25 PASS lines. Its failure is retained. The blocked individual filters are attachment-file, markdown-file, text, startup and path-links. No privilege/security setting was changed and no failed fixture was called a pass.

## Pending

- Further selected WPF backup/recovery/attachments/output/OCR regression and independent comparison of newly emitted PDF fixtures.
- Actual installed/self-contained-EXE validation with safe test arguments; the old portable-only installed test is not safe for this scope because it accesses the ordinary UI identity.
- Physical Korean IME, physical mixed-monitor DPI and manual desktop/window interaction. Global input/clipboard use awaits coordination with the other PC task; the current execution context has no exposed supported physical Windows input tool.
- Visual fidelity and remaining usability issues against the adopted concept. Three areas and writing/status access are present; the existing layout is not asserted pixel-identical to the concept.
- Full suites retain symbolic-link environment constraints. No whole-product completion or acceptance claim.

Only source, test code, this scoped checkpoint and the small concept asset belong in Git. Actual notes, recovery secrets, identifiers from trial runs, credentials, signing keys, SDKs, packages, native binaries, logs and machine-specific paths stay outside the repository. Final binaries will be delivered separately only after their applicable verification.

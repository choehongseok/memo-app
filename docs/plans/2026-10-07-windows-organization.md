# Windows organization, search, trash, history and encrypted backup

## Approved outcome and limits
Continue the existing 134-feature memo app in the cloud using synthetic data. Implement and test independent functionality until a genuine dependency on the user's Windows test is reached. Keep Windows GUI/IME/SessionLock/ACL verification distinct from automated Core/CI evidence. Keep the existing draft PR; do not merge or publicly deploy. Preserve the exact original prompt and all feature IDs.

## Design
Extend the encrypted snapshot payload to schema 2 without changing the reviewed AES-GCM envelope or key/nonce rules. Read schema 1 without mutating disk; normalize it in memory. The first schema 2 write uses the existing expected-base fingerprint, flushed pending file and atomic replacement with a unique preserved previous snapshot. Refuse unsupported schemas, malformed relationships and budgets before preparing a write. Do not silently overwrite an older file or downgrade schema 2.

Schema 2 adds encrypted folders and tags (UUID identities), encrypted per-note organization metadata, and metadata on immutable revisions. Note metadata includes folder/tag IDs, a bounded color choice, important/favorite/list-pinned/archive/order/deleted flags. Deleted notes retain full content in the encrypted notes array; their current deletion revision is represented by a matching tombstone. A user restore creates a new revision, retains the deletion event in history, removes the current tombstone, and never changes another note. Legacy content-less tombstones remain reserved and cannot be resurrected implicitly. Permanent deletion and automatic emptying are excluded from this unit.

Folders reject duplicates, missing parents and cycles; notes reject unknown folder/tag references. Folder/tag count is capped at 100 each and names at 128 UTF-16 units. Trial notes (including trash) remain capped at 100, history at 512 per note/10000 total, and the envelope at 16 MiB. Metadata-only edits create revisions; no-op edits do not. Tags removed from a note remain in the encrypted tag catalog until an explicit future cleanup feature.

Search is literal, case-insensitive and Unicode-normalized (NFC) in unlocked memory only. Support title/body/all, folder with descendants, tags, archive/trash, favorite/important, dates and stable sort. Never use a persistent plaintext index. Search results reference the current shared draft, so edits remain consistent across windows. Locked UI clears queries, results, folder/tag lists, history previews and child-window bindings before save I/O waits; Core closes retained editing references.

Manual backup waits for a successful latest save, reads only the committed fingerprint-matching ciphertext, and writes a new destination via CreateNew+flush. No plaintext export, implicit overwrite or external transmission. Reject source/destination reparse points; same-vault candidate destinations and unfinished/faulted sessions cannot be backed up. A failure preserves the original and any partial destination. Restore continues the authenticated bounded import/candidate flow. Test an actual backup copied to a separate synthetic root, then reopen/decrypt it with the recovery secret.

## Implementation sequence and tests
1. Verify baseline; add failing behavior tests for memory search and organization API.
2. Obtain independent storage-design review and implement schema validation/migration, revision/trash/metadata operations.
3. Add actual encrypted restart, v1 migration previous-byte preservation, malformed schema 2, trash restore/no-resurrection, history branch and backup recovery/failure tests; run the full contract suite.
4. Wire existing WPF management/sticky UI to folders, search, organization, trash, history and backup; cross-build. UI remains unverified until Windows execution.
5. Update relevant ledger evidence without claiming user acceptance, run original/134-ID/Python/safety/build/contract/publish checks, commit small units and update the draft branch. Check CI on the exact pushed commit.
6. Continue independent phase 2 Windows features and only then assess sync/Android/local-engine dependencies; this unit is not the final stopping condition.

## Review status
Independent read-only design/code review completed against baseline 86e633e and the actual implementation diff. Required strict v1/v2 fields, matching tombstones, immutable tags, fingerprint/create-new backup, frozen latest accepted history and full hidden organization, epoch guards and complete UI undo clearing were applied. Review found two concrete issues: fast delete/restore lost deletion events; event rejection at the history limit mutated drafts. The implementation now preflights a whole next snapshot before Stage→Accept→Publish and tests rapid events and rejection invariants. Reviewer read the test code and author-run evidence but did not independently run it. WindowsChecks execution is pending CI. This is not a professional security audit or a full 134-feature acceptance.

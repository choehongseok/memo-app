using MemoApp.Core.Documents;
using MemoApp.Core.Storage;

namespace MemoApp.Core.Editing;

internal sealed class PreparedRichImageTextEdit(EditingWorkspace owner, VaultSnapshot basis,
    NoteDraft note, (NoteDraft Note, long Version)[] membership, long epoch, StoredNote candidate,
    long editVersion, long contentVersion)
{
    internal readonly EditingWorkspace Owner = owner;
    internal readonly VaultSnapshot Basis = basis;
    internal readonly NoteDraft Note = note;
    internal readonly (NoteDraft Note, long Version)[] Membership = membership;
    internal readonly long Epoch = epoch;
    internal readonly StoredNote Candidate = candidate;
    internal readonly long EditVersion = editVersion, ContentVersion = contentVersion;
    internal bool Applied, Consumed;
}

public sealed partial class EditingWorkspace
{
    private bool richImageTextPublishing;
    internal bool IsRichImageTextPublicationActive => richImageTextPublishing;
    internal Guid RichImageTextRootId => attachmentRootId;
    internal bool IsRichImageTextHandoffCurrent(PreparedRichImageTextEdit prepared)
    {
        if (closed || !ReferenceEquals(prepared.Owner,this) || !ReferenceEquals(basis,prepared.Basis)
            || ocrAuthorityEpoch != prepared.Epoch+1 || notes.Count != prepared.Membership.Length) return false;
        foreach (var member in prepared.Membership)
            if (!notes.Contains(member.Note) || member.Note.EditVersion != member.Version+(ReferenceEquals(member.Note,prepared.Note)?1:0)) return false;
        return true;
    }

    private bool RichImageTextBasisCurrent(PreparedRichImageTextEdit prepared, long epoch)
    {
        if (closed || !ReferenceEquals(prepared.Owner, this) || !ReferenceEquals(basis, prepared.Basis)
            || ocrAuthorityEpoch != epoch || notes.Count != prepared.Membership.Length || prepared.Note.IsClosed
            || prepared.Note.IsDeleted || prepared.Note.ContentVersion != prepared.ContentVersion) return false;
        for (int i = 0; i < notes.Count; i++)
            if (!ReferenceEquals(notes[i], prepared.Membership[i].Note) || notes[i].EditVersion != prepared.Membership[i].Version) return false;
        return true;
    }

    internal PreparedRichImageTextEdit PrepareRichImageTextEdit(NoteDraft note, StyledDocument document)
    {
        RequireNote(note);
        if (richImageTextPublishing || mergePublishing || ocrPublishing) throw new InvalidOperationException("Text publication occupied");
        if (note.Mode != "rich" || note.Document?.SchemaVersion != 2 || document.SchemaVersion != 2)
            throw new InvalidOperationException("Known v2 text source required");
        var info = RichDocumentCodec.Inspect(document);
        if (!info.Supported) throw new InvalidDataException("Complete supported text candidate required");
        var expected = basis; long epoch = ocrAuthorityEpoch;
        var membership = notes.Select(n => (Note: n, Version: n.EditVersion)).ToArray();
        long editVersion = note.EditVersion, contentVersion = note.ContentVersion;
        _ = checked(editVersion + 1); _ = checked(contentVersion + 1); _ = checked(epoch + 1);
        var before = Capture(); var current = before.Notes.Single(n => n.NoteId == note.Id);
        var history = before.History.ToList();
        if (acceptedVersions.TryGetValue(note.Id, out long accepted) && accepted == editVersion)
        {
            history.Add(new(current.NoteId, current.RevisionId, (Guid[])current.Parents.Clone(), current.ModifiedAt, current.Title, current.Text)
            { Metadata = current.Metadata, Mode = current.Mode, Document = current.Document, AttachmentIds = current.AttachmentIds });
            current = current with { RevisionId = Guid.NewGuid(), Parents = [current.RevisionId] };
        }
        current = current with { Text = info.Text!, Document = document, ModifiedAt = clock.GetUtcNow() };
        var candidate = before with { Notes = before.Notes.Select(n => n.NoteId == note.Id ? current : n).ToArray(), History = history.ToArray() };
        // Validate the prospective saved state, including duplicated schema-11 historical occurrences.
        VaultEnvelope.Validate(candidate);
        DiscardedEvidence.RequirePreserved(discardedRevisions, discardedMarkers, candidate);
        var prepared = new PreparedRichImageTextEdit(this, expected, note, membership, epoch, current, editVersion, contentVersion);
        if (!RichImageTextBasisCurrent(prepared, epoch)) throw new InvalidOperationException("Text basis changed during preparation");
        return prepared;
    }

    internal bool ApplyPreparedRichImageTextEdit(PreparedRichImageTextEdit prepared, Func<bool> current, Action markApplied)
    {
        if (prepared.Consumed) throw new InvalidOperationException("Text candidate consumed");
        prepared.Consumed = true;
        bool Live(long epoch)
        {
            if (!RichImageTextBasisCurrent(prepared, epoch)) return false;
            bool result = current();
            return result && RichImageTextBasisCurrent(prepared, epoch);
        }
        if (richImageTextPublishing || mergePublishing || ocrPublishing || !Live(prepared.Epoch))
            throw new InvalidOperationException("Text authority ended");
        var invalidations = AttachmentReadInvalidating?.GetInvocationList() ?? [];
        var observers = Changed?.GetInvocationList() ?? [];
        // Allocate notifications before invalidation; no callback or allocation in the field swap.
        var notifications = prepared.Note.PrepareRichImageTextNotifications();
        if (!Live(prepared.Epoch)) throw new InvalidOperationException("Text authority ended");
        richImageTextPublishing = true;
        try
        {
            ocrAuthorityEpoch = prepared.Epoch + 1;
            foreach (Action<NoteDraft?> handler in invalidations)
            { handler(prepared.Note); if (!Live(prepared.Epoch + 1)) throw new InvalidOperationException("Text invalidation changed authority"); }
            if (!Live(prepared.Epoch + 1)) throw new InvalidOperationException("Text publication changed authority");
            prepared.Note.StageRichImageTextDraft(prepared.Candidate, prepared.EditVersion + 1, prepared.ContentVersion + 1);
            prepared.Applied = true;
            // Trusted inert coordinator marker. Applied remains truthful even if an internal observer fails.
            try { markApplied(); } catch { return false; }
            bool success = true;
            foreach (Action handler in observers)
            { try { handler(); } catch { success = false; } if (closed) { success = false; break; } }
            if (!closed) { try { prepared.Note.PublishRichImageTextDraft(notifications); } catch { success = false; } }
            else success = false;
            return success;
        }
        finally { richImageTextPublishing = false; }
    }
}

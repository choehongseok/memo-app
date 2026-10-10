using System.Collections.Immutable;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Storage;

namespace MemoApp.Core.Editing;

internal sealed record RichImageTextBlock(int BlockIndex, Guid AttachmentId, string Alt, string RawNode);
internal sealed class RichImageTextEditContext
{
    private RichImageTextEditContext(StyledDocument document, ImmutableArray<RichImageTextBlock> images)
    { OriginalDocument = document; Images = images; }
    internal StyledDocument OriginalDocument { get; }
    internal ImmutableArray<RichImageTextBlock> Images { get; }
    // This inert value is never authority without exact membership in the original issuer registry.
    internal static RichImageTextEditContext Create(StyledDocument document, ImmutableArray<RichImageTextBlock> images) => new(document, images);
}
internal enum RichImageTextEditOutcome { NotApplied, NoChange, AppliedDirty }
internal sealed class RichImageTextEditReceipt(RichImageTextEditOutcome outcome, RichImageTextEditContext? continuation, bool notificationSucceeded)
{
    internal RichImageTextEditOutcome Outcome { get; } = outcome;
    internal RichImageTextEditContext? Continuation { get; } = continuation;
    internal bool NotificationSucceeded { get; } = notificationSucceeded;
}

public sealed partial class SaveCoordinator
{
    private sealed class RichImageTextEntry(RichImageTextEditContext context, EditingWorkspace workspace, NoteDraft note,
        long session, long preview, Guid root, ImmutableArray<StoredAttachmentObject> objects,
        long editVersion, long contentVersion)
    {
        internal readonly RichImageTextEditContext Context = context;
        internal readonly EditingWorkspace Workspace = workspace;
        internal readonly NoteDraft Note = note;
        internal readonly long Session = session, Preview = preview, EditVersion = editVersion, ContentVersion = contentVersion;
        internal readonly Guid Root = root;
        internal readonly ImmutableArray<StoredAttachmentObject> Objects = objects;
        internal bool Retired, Consumed, Active = true, OwnInvalidation;
        internal PreparedRichImageTextEdit? Prepared;
    }
    private readonly Dictionary<RichImageTextEditContext, RichImageTextEntry> richImageTextEntries = [];
    private RichImageTextEntry? richImageTextAttempt;
    private Func<bool>? richImageTextCheckAccess;
    private const int MaxRichImageTextContexts = 128;

    internal void RegisterRichImageTextEditOwner(Func<bool> checkAccess)
    {
        ArgumentNullException.ThrowIfNull(checkAccess);
        if (richImageTextCheckAccess is not null)
        {
            RequireRichImageTextOwner();
            if (!richImageTextCheckAccess.Equals(checkAccess)) throw new InvalidOperationException("Text owner policy cannot be replaced");
            return;
        }
        if (!checkAccess()) throw new InvalidOperationException("Text owner access required");
        richImageTextCheckAccess = checkAccess;
    }
    private void RequireRichImageTextOwner()
    { if (richImageTextCheckAccess?.Invoke() != true) throw new InvalidOperationException("Text owner access required"); }

    private bool RichImageTextSourceCurrent(RichImageTextEntry entry, StyledDocument document, long edit, long content, long preview)
    {
        if (disposed || IsLocked || vault.IsFaulted || entry.Retired || !ReferenceEquals(Workspace, entry.Workspace)
            || sessionEpoch != entry.Session || AttachmentPreviewEpoch != preview || entry.Note.IsClosed || entry.Note.IsDeleted
            || entry.Note.EditVersion != edit || entry.Note.ContentVersion != content || !ReferenceEquals(entry.Note.Document, document)
            || entry.Note.Mode != "rich" || Workspace.RichImageTextRootId != entry.Root) return false;
        try
        {
            Workspace.RequireAttachmentNote(entry.Note);
            if (!vault.IsCurrentAnchoredAttachmentRoot(entry.Root)) return false;
            foreach (var item in entry.Objects)
                if (!ReferenceEquals(item, Workspace.AttachmentObject(entry.Note, item.ObjectId)) || !vault.IsKnownAuthenticatedAttachmentDescriptor(item)) return false;
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }
    internal bool IsRichImageTextEditContextCurrent(RichImageTextEditContext context)
    {
        RequireRichImageTextOwner();
        return richImageTextEntries.TryGetValue(context, out var entry) && entry.Active && !entry.Consumed
            && RichImageTextSourceCurrent(entry, context.OriginalDocument, entry.EditVersion, entry.ContentVersion, entry.Preview);
    }
    internal RichImageTextEditContext CaptureRichImageTextEditContext(NoteDraft note, long expectedEditVersion, long expectedPreviewEpoch)
    {
        RequireRichImageTextOwner();
        foreach (var pair in richImageTextEntries.ToArray()) if (pair.Value.Retired && !ReferenceEquals(pair.Value, richImageTextAttempt)) richImageTextEntries.Remove(pair.Key);
        if (richImageTextAttempt is not null || richImageTextEntries.Count >= MaxRichImageTextContexts)
            throw new InvalidOperationException("Text context admission occupied");
        RequireAttachmentSource(note, expectedEditVersion, sessionEpoch);
        if (expectedPreviewEpoch != AttachmentPreviewEpoch || note.Mode != "rich" || note.Document?.SchemaVersion != 2)
            throw new InvalidOperationException("Original v2 text authority required");
        var info = RichDocumentCodec.Inspect(note.Document);
        if (!info.Supported || info.Text != note.Text) throw new InvalidDataException("Complete v2 source required");
        var images = ReadRichImageTextBlocks(note.Document);
        var objects = images.Select(image => Workspace.AttachmentObject(note, image.AttachmentId)).Distinct().ToImmutableArray();
        var context = RichImageTextEditContext.Create(note.Document, images);
        var entry = new RichImageTextEntry(context, Workspace, note, sessionEpoch, expectedPreviewEpoch,
            Workspace.RichImageTextRootId, objects, note.EditVersion, note.ContentVersion);
        if (!RichImageTextSourceCurrent(entry, context.OriginalDocument, entry.EditVersion, entry.ContentVersion, entry.Preview))
            throw new InvalidOperationException("Original known attachment root/object authority required");
        RequireRichImageTextOwner();
        if (!RichImageTextSourceCurrent(entry, context.OriginalDocument, entry.EditVersion, entry.ContentVersion, entry.Preview))
            throw new InvalidOperationException("Text capture source changed");
        richImageTextEntries.Add(context, entry);
        return context;
    }
    internal void RetireRichImageTextEditContext(RichImageTextEditContext context)
    {
        RequireRichImageTextOwner();
        if (!richImageTextEntries.TryGetValue(context, out var entry)) return;
        entry.Retired = true;
        if (!ReferenceEquals(richImageTextAttempt, entry)) richImageTextEntries.Remove(context);
    }
    private void ObserveRichImageTextRevocation(NoteDraft? source)
    {
        var own = richImageTextAttempt;
        bool permit = own is { Retired: false, OwnInvalidation: false, Prepared: { Applied: false } }
            && Workspace.IsRichImageTextPublicationActive && ReferenceEquals(source, own.Note)
            && AttachmentPreviewEpoch == own.Preview + 1 && sessionEpoch == own.Session
            && ReferenceEquals(own.Note.Document, own.Context.OriginalDocument)
            && own.Note.EditVersion == own.EditVersion && own.Note.ContentVersion == own.ContentVersion;
        // .NET dictionary removal does not invalidate enumeration. Drop owner-held plaintext/source
        // references immediately; only the exact real attempt survives until its finally block.
        foreach (var pair in richImageTextEntries)
        {
            var entry = pair.Value;
            if (permit && ReferenceEquals(entry, own)) { entry.OwnInvalidation = true; continue; }
            entry.Retired = true;
            if (!ReferenceEquals(entry, own)) richImageTextEntries.Remove(pair.Key);
        }
    }
    internal bool IsRichImageTextEditOwnTransition(RichImageTextEditContext context, StyledDocument observed,
        long editVersion, long contentVersion, long previewEpoch)
    {
        RequireRichImageTextOwner();
        if (!richImageTextEntries.TryGetValue(context, out var entry) || !ReferenceEquals(entry, richImageTextAttempt)
            || entry.Prepared is not { } prepared || entry.Retired) return false;
        if (prepared.Applied)
            return entry.OwnInvalidation && ReferenceEquals(observed, prepared.Candidate.Document)
                && editVersion == entry.EditVersion + 1 && contentVersion == entry.ContentVersion + 1
                && previewEpoch == entry.Preview + 1 && Workspace.IsRichImageTextHandoffCurrent(prepared)
                && RichImageTextSourceCurrent(entry, observed, editVersion, contentVersion, previewEpoch);
        return ReferenceEquals(observed, context.OriginalDocument) && editVersion == entry.EditVersion
            && contentVersion == entry.ContentVersion && previewEpoch == entry.Preview + (entry.OwnInvalidation ? 1 : 0)
            && RichImageTextSourceCurrent(entry, observed, editVersion, contentVersion, previewEpoch);
    }
    internal RichImageTextEditReceipt ApplyRichImageTextEdit(RichImageTextEditContext context, StyledDocument candidate)
    {
        RequireRichImageTextOwner();
        var refused = new RichImageTextEditReceipt(RichImageTextEditOutcome.NotApplied, null, false);
        if (!richImageTextEntries.TryGetValue(context, out var entry) || entry.Consumed) return refused;
        // Every own attempt is single-use, including invalid candidate and reentrant occupied attempts.
        bool wasCurrent = IsRichImageTextEditContextCurrent(context); entry.Consumed = true;
        if (!wasCurrent || richImageTextAttempt is not null)
        {
            entry.Retired = true;
            if (!ReferenceEquals(entry, richImageTextAttempt)) richImageTextEntries.Remove(context);
            return refused;
        }
        RichImageTextEntry? next = null;
        PreparedRichImageTextEdit? prepared = null;
        var appliedWithoutContinuation = new RichImageTextEditReceipt(RichImageTextEditOutcome.AppliedDirty, null, false);
        try
        {
            if (candidate.SchemaVersion != 2 || !RichDocumentCodec.Inspect(candidate).Supported)
                throw new InvalidDataException("Supported v2 candidate required");
            var images = ReadRichImageTextBlocks(candidate);
            if (images.Length != context.Images.Length) throw new InvalidDataException("Image multiplicity changed");
            for (int i = 0; i < images.Length; i++)
                if (images[i].AttachmentId != context.Images[i].AttachmentId || images[i].Alt != context.Images[i].Alt
                    || images[i].RawNode != context.Images[i].RawNode) throw new InvalidDataException("Immutable canonical image changed");
            bool same;
            using (var original = JsonDocument.Parse(context.OriginalDocument.SourceJson))
            using (var proposed = JsonDocument.Parse(candidate.SourceJson)) same = JsonElement.DeepEquals(original.RootElement, proposed.RootElement);
            var continuation = RichImageTextEditContext.Create(same ? context.OriginalDocument : candidate, images);
            next = new(continuation, entry.Workspace, entry.Note, entry.Session, checked(entry.Preview + (same ? 0 : 1)), entry.Root,
                entry.Objects, checked(entry.EditVersion + (same ? 0 : 1)), checked(entry.ContentVersion + (same ? 0 : 1))) { Active = false };
            // Bound continuation admission and allocate dictionary growth before callbacks/publication.
            richImageTextEntries.EnsureCapacity(richImageTextEntries.Count + 1);
            var success = new RichImageTextEditReceipt(same ? RichImageTextEditOutcome.NoChange : RichImageTextEditOutcome.AppliedDirty, continuation, true);
            if (same)
            {
                RequireRichImageTextOwner();
                if (!RichImageTextSourceCurrent(entry, context.OriginalDocument, entry.EditVersion, entry.ContentVersion, entry.Preview)) return refused;
                next.Active = true; richImageTextEntries.Add(continuation, next); return success;
            }
            prepared = Workspace.PrepareRichImageTextEdit(entry.Note, candidate); entry.Prepared = prepared;
            richImageTextAttempt = entry;
            bool notifications = Workspace.ApplyPreparedRichImageTextEdit(prepared,
                () => { RequireRichImageTextOwner(); return IsRichImageTextEditOwnTransition(context, entry.Note.Document!, entry.Note.EditVersion, entry.Note.ContentVersion, AttachmentPreviewEpoch); },
                () => { });
            if (!prepared.Applied) return refused;
            RequireRichImageTextOwner();
            if (!notifications || !IsRichImageTextEditOwnTransition(context, entry.Note.Document!, entry.Note.EditVersion, entry.Note.ContentVersion, AttachmentPreviewEpoch))
                return appliedWithoutContinuation;
            // Capacity and all values were allocated before invalidation. This callback-free insertion
            // happens only after the exact final handoff; no pending authority survives lock/retirement.
            next.Active = true; richImageTextEntries.Add(continuation, next); return success;
        }
        catch
        { return prepared?.Applied == true ? appliedWithoutContinuation : refused; }
        finally
        {
            if (ReferenceEquals(richImageTextAttempt, entry)) richImageTextAttempt = null;
            entry.Retired = true; richImageTextEntries.Remove(context);
            if (next is { Active: false }) richImageTextEntries.Remove(next.Context);
        }
    }
    private static ImmutableArray<RichImageTextBlock> ReadRichImageTextBlocks(StyledDocument document)
    {
        var known = RichDocumentCodec.Images(document);
        using var parsed = JsonDocument.Parse(document.SourceJson);
        var nodes = parsed.RootElement.GetProperty("nodes");
        return known.Select(image => new RichImageTextBlock(image.BlockIndex, image.AttachmentId, image.Alt, nodes[image.BlockIndex].GetRawText())).ToImmutableArray();
    }
}

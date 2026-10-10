using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MemoApp.Core.Documents;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;

namespace MemoApp.Core.Editing;

public sealed partial class SaveCoordinator
{
    private sealed class WordExportEntry(EditingWorkspace workspace, long epoch, long preview,
        NoteDraft[] notes, long[] versions, CancellationToken token)
    {
        internal readonly EditingWorkspace Workspace = workspace;
        internal readonly long Epoch = epoch, Preview = preview;
        internal readonly NoteDraft[] Notes = notes;
        internal readonly long[] Versions = versions;
        internal readonly CancellationToken Token = token;
        internal readonly List<(NoteDraft Note, StoredAttachmentObject Object)> Objects = [];
        internal OwnedWordImageContext? Context;
        internal bool Revoked;
    }
    private WordExportEntry? wordExport;
    private bool wordCapturing;
    private Func<bool>? wordCheckAccess;
    // Trusted stable platform policy: WPF Dispatcher.CheckAccess; not a renewable source grant.
    internal void RegisterWordImageExportOwner(Func<bool> checkAccess)
    {
        ArgumentNullException.ThrowIfNull(checkAccess);
        if (wordCheckAccess is not null)
        {
            EnsureWordExportOwner();
            if (!wordCheckAccess.Equals(checkAccess)) throw new InvalidOperationException("Word owner policy cannot be replaced");
            return;
        }
        if (!checkAccess()) throw new InvalidOperationException("Word owner access required");
        wordCheckAccess = checkAccess;
    }
    private void EnsureWordExportOwner()
    { if (wordCheckAccess?.Invoke() != true) throw new InvalidOperationException("Word owner access required"); }
    private void RequireWordExportCurrent(WordExportEntry entry)
    {
        EnsureWordExportOwner();
        entry.Token.ThrowIfCancellationRequested();
        if (disposed || IsLocked || vault.IsFaulted || entry.Revoked || !ReferenceEquals(wordExport, entry)
            || !ReferenceEquals(Workspace, entry.Workspace) || sessionEpoch != entry.Epoch
            || AttachmentPreviewEpoch != entry.Preview)
            throw new InvalidOperationException("Word export original owner authority ended");
        for (int i = 0; i < entry.Notes.Length; i++)
            RequireAttachmentSource(entry.Notes[i], entry.Versions[i], entry.Epoch);
        foreach (var pair in entry.Objects)
            if (!ReferenceEquals(pair.Object, Workspace.AttachmentObject(pair.Note, pair.Object.ObjectId)))
                throw new InvalidOperationException("Word export immutable source changed");
        if (entry.Objects.Count != 0 && !vault.AttachmentRootAnchored)
            throw new InvalidOperationException("Word export requires the original authenticated attachment root");
    }
    internal bool IsWordImageContextCurrent(OwnedWordImageContext context)
    {
        EnsureWordExportOwner();
        if (wordExport is not { } entry || !ReferenceEquals(entry.Context, context) || context.IsRevoked) return false;
        try { RequireWordExportCurrent(entry); return true; }
        catch (InvalidOperationException) { return false; }
        catch (OperationCanceledException) { return false; }
    }
    private void RevokeWordImageContext()
    {
        if (wordExport is not { } entry) return;
        EnsureWordExportOwner();
        entry.Revoked = true; entry.Context?.Dispose();
        if (!wordCapturing && entry.Context?.IsSettled == true) wordExport = null;
    }
    internal void RetireWordImageContext(OwnedWordImageContext context)
    {
        EnsureWordExportOwner();
        ArgumentNullException.ThrowIfNull(context);
        if (wordExport is not { } entry || !ReferenceEquals(entry.Context, context))
            throw new InvalidOperationException("Word context was not issued by this owner");
        RevokeWordImageContext();
    }
    internal void SettleWordImageContext(OwnedWordImageContext context)
    {
        EnsureWordExportOwner();
        ArgumentNullException.ThrowIfNull(context);
        if (wordExport is not { } entry || !ReferenceEquals(entry.Context, context))
        {
            if (context.IsSettled) return;
            throw new InvalidOperationException("Word context was not issued by this owner");
        }
        if (!context.IsSettled) throw new InvalidOperationException("Word worker has not actually settled");
        entry.Revoked = true; context.Dispose(); wordExport = null;
    }
    internal OwnedWordImageContext CaptureWordImageContext(IReadOnlyList<NoteDraft> selection,
        IReadOnlyList<long> expectedVersions, long expectedPreviewEpoch, CancellationToken token,
        Action<byte[]>? allocations = null)
    {
        EnsureWordExportOwner();
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(expectedVersions);
        // Reserve before authenticating source, allocating leases or plaintext copies. Never prune an active worker.
        if (wordCapturing || wordExport is { Context: { } previous } && (!previous.IsRevoked || !previous.IsSettled))
            throw new InvalidOperationException("One outstanding Word context is already issued");
        if (wordExport is { } retired)
        { retired.Revoked = true; retired.Context?.Dispose(); wordExport = null; }
        if (selection.Count is < 1 or > 100 || expectedVersions.Count != selection.Count)
            throw new ArgumentException("Word export selection limits");
        wordCapturing = true;
        WordExportEntry? entry = null;
        byte[][] owned = [];
        bool returned = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var notes = selection.ToArray(); var versions = expectedVersions.ToArray();
            if (notes.Any(n => n is null) || notes.Select(n => n.Id).Distinct().Count() != notes.Length)
                throw new ArgumentException("Word export distinct active selection required");
            entry = new(Workspace, sessionEpoch, expectedPreviewEpoch, notes, versions, token); wordExport = entry;
            RequireWordExportCurrent(entry);
            var capturedNotes = ImmutableArray.CreateBuilder<WordNoteSource>(notes.Length);
            var placements = ImmutableArray.CreateBuilder<WordImagePlacement>();
            var unique = new List<(NoteDraft Note, StoredAttachmentObject Object, long Version)>();
            var objects = new Dictionary<Guid, StoredAttachmentObject>();
            long sourceBytes = 0, canonicalBytes = 0, mediaBytes = 0;
            var utf8 = new UTF8Encoding(false, true);
            for (int i = 0; i < notes.Length; i++)
            {
                token.ThrowIfCancellationRequested(); RequireWordExportCurrent(entry);
                var note = notes[i];
                using (var text = TextTransfer.Capture(note)) sourceBytes = checked(sourceBytes + text.Bytes.Length);
                if (sourceBytes > 16 * 1024 * 1024) throw new InvalidDataException("Word source byte limit");
                XmlConvert.VerifyXmlChars(note.Title); XmlConvert.VerifyXmlChars(note.Text);
                if (note.Mode == "rich")
                {
                    var info = RichDocumentCodec.Inspect(note.Document!);
                    if (!info.Supported || info.Text != note.Text) throw new InvalidDataException("Word complete canonical source projection required");
                    canonicalBytes = checked(canonicalBytes + utf8.GetByteCount(note.Document!.SourceJson));
                    if (canonicalBytes > 16 * 1024 * 1024) throw new InvalidDataException("Word canonical source byte limit");
                    foreach (var image in RichDocumentCodec.Images(note.Document))
                    {
                        XmlConvert.VerifyXmlChars(image.Alt);
                        var item = Workspace.AttachmentObject(note, image.AttachmentId);
                        if (!vault.AttachmentRootAnchored || item.Mime != "image/png")
                            throw new InvalidOperationException("Word image requires authenticated existing PNG root and reference");
                        AttachmentValidation.Description(item.Name, item.Mime, item.Length, item.Sha256);
                        if (item.Length < 8 || item.RootId == Guid.Empty) throw new InvalidDataException("Word PNG descriptor limits");
                        if (!entry.Objects.Any(p => ReferenceEquals(p.Note, note) && p.Object.ObjectId == item.ObjectId))
                            entry.Objects.Add((note, item));
                        if (objects.TryGetValue(item.ObjectId, out var same))
                        {
                            if (!ReferenceEquals(item, same)) throw new InvalidDataException("Word repeated immutable object differs");
                        }
                        else
                        {
                            if (objects.Count >= 128) throw new InvalidDataException("Word media count limit");
                            mediaBytes = checked(mediaBytes + item.Length);
                            if (mediaBytes > 8 * 1024 * 1024) throw new InvalidDataException("Word unique media byte limit");
                            objects.Add(item.ObjectId, item); unique.Add((note, item, versions[i]));
                        }
                        if (placements.Count >= 102400) throw new InvalidDataException("Word image placement limit");
                        placements.Add(new(i, image.BlockIndex, item.ObjectId, image.Alt));
                    }
                }
                capturedNotes.Add(new(note.Title, note.Text, note.Mode, note.Document));
            }
            RequireWordExportCurrent(entry);
            // Entire metadata/reference/byte budget is fixed before the first authenticated read.
            owned = new byte[unique.Count][];
            var descriptors = ImmutableArray.CreateBuilder<WordImageDescriptor>(unique.Count);
            long sourcePixels = 0;
            for (int i = 0; i < unique.Count; i++)
            {
                token.ThrowIfCancellationRequested(); RequireWordExportCurrent(entry);
                var (note, item, version) = unique[i]; int index = i;
                using var lease = CreateAttachmentReadLease(note, item.ObjectId, version);
                bool accepted = lease.Consume(bytes =>
                {
                    token.ThrowIfCancellationRequested(); RequireWordExportCurrent(entry);
                    if (bytes.Length != item.Length || Convert.ToHexStringLower(SHA256.HashData(bytes)) != item.Sha256)
                        throw new InvalidDataException("Word exact authenticated media descriptor changed");
                    var header = PngPreviewProfile.Inspect(bytes);
                    sourcePixels = checked(sourcePixels + header.SourcePixels);
                    if (sourcePixels > 64_000_000) throw new InvalidDataException("Word unique PNG source pixel budget");
                    owned[index] = bytes.ToArray(); allocations?.Invoke(owned[index]);
                    token.ThrowIfCancellationRequested(); RequireWordExportCurrent(entry);
                    descriptors.Add(new(item.ObjectId, item.RootId, item.Sha256, item.Length, header.Width, header.Height, header.SourcePixels));
                });
                if (!accepted) throw new InvalidOperationException("Word authenticated read was revoked");
                RequireWordExportCurrent(entry);
            }
            token.ThrowIfCancellationRequested(); RequireWordExportCurrent(entry);
            var context = OwnedWordImageContext.TakeCapturedOwnership(capturedNotes.ToImmutable(), descriptors.ToImmutable(), placements.ToImmutable(), owned);
            entry.Context = context; returned = true; return context;
        }
        finally
        {
            wordCapturing = false;
            if (!returned)
            {
                foreach (var bytes in owned) if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
                if (entry is null || ReferenceEquals(wordExport, entry)) wordExport = null;
            }
        }
    }
}

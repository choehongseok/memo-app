using System.Collections.Immutable;
using System.Security.Cryptography;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;

namespace MemoApp.Core.Transfer;

internal sealed record WordNoteSource(string Title, string Text, string Mode, StyledDocument? Document);
internal sealed record WordImageDescriptor(Guid ObjectId, Guid RootId, string Sha256, int Length,
    int Width, int Height, int SourcePixels);
internal sealed record WordImagePlacement(int NoteIndex, int BlockIndex, Guid ObjectId, string Alt);

// Closed captured data and buffer lifetime only. It carries no source owner or write authority.
internal sealed class OwnedWordImageContext : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationToken token;
    private byte[][] bytes;
    private Task? completion, settled;
    private bool started, reading, cleaned, revoked;
    internal ImmutableArray<WordNoteSource> Notes { get; }
    internal ImmutableArray<WordImageDescriptor> Images { get; }
    internal ImmutableArray<WordImagePlacement> Placements { get; }
    internal CancellationToken Token => token;
    internal bool IsRevoked { get { lock (gate) return revoked; } }
    internal bool IsSettled
    {
        get
        {
            lock (gate) return cleaned && (!started ||
                completion!.IsCompleted && settled!.IsCompletedSuccessfully);
        }
    }
    private OwnedWordImageContext(ImmutableArray<WordNoteSource> notes,
        ImmutableArray<WordImageDescriptor> images, ImmutableArray<WordImagePlacement> placements,
        byte[][] ownedBytes)
    { Notes = notes; Images = images; Placements = placements; bytes = ownedBytes; token = cancellation.Token; }
    // Source issuer alone validates descriptors and authenticates each buffer before transfer.
    internal static OwnedWordImageContext TakeCapturedOwnership(ImmutableArray<WordNoteSource> notes,
        ImmutableArray<WordImageDescriptor> images, ImmutableArray<WordImagePlacement> placements,
        byte[][] ownedBytes) => new(notes, images, placements, ownedBytes);

    // Fixed internal builder binds preallocated actual terminal tasks before queueing its worker.
    internal void BeginBuild(Task actualCompletion, Task actualSettled)
    {
        ArgumentNullException.ThrowIfNull(actualCompletion);
        ArgumentNullException.ThrowIfNull(actualSettled);
        lock (gate)
        {
            if (started || revoked || cleaned) throw new InvalidOperationException("Word context ended or was already started");
            completion = actualCompletion; settled = actualSettled; started = true;
        }
    }
    internal void ReadImage(Guid objectId, AttachmentByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte[] borrowed;
        lock (gate)
        {
            if (!started || cleaned || revoked || reading) throw new InvalidOperationException("Word image borrow is unavailable");
            int index = -1;
            for (int i = 0; i < Images.Length; i++) if (Images[i].ObjectId == objectId) { index = i; break; }
            if (index < 0) throw new InvalidOperationException("Word image is not in the captured context");
            reading = true; borrowed = bytes[index];
        }
        try { reader(borrowed); }
        catch { Dispose(); throw; }
        finally { lock (gate) reading = false; }
    }
    // Only fixed worker teardown calls this, after decoder and ZIP streams have disposed.
    internal bool CompleteBuild()
    {
        lock (gate)
        {
            if (!started || cleaned || reading) throw new InvalidOperationException("Word build teardown is unavailable");
            bool success = !revoked; Clear(); if (revoked) cancellation.Dispose(); return success;
        }
    }
    private void Clear()
    {
        foreach (var owned in bytes) CryptographicOperations.ZeroMemory(owned);
        bytes = []; cleaned = true;
    }
    public void Dispose()
    {
        lock (gate)
        {
            revoked = true;
            if (!started && !cleaned) Clear();
        }
        // Cancellation observers have no authority to prevent revocation/retirement.
        try { cancellation.Cancel(); } catch (AggregateException) { } catch (ObjectDisposedException) { }
        lock (gate) if (!started || cleaned) cancellation.Dispose();
    }
}

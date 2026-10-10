using System.Security.Cryptography;
namespace MemoApp.Core.Editing;

public delegate void AttachmentByteReader(ReadOnlySpan<byte> bytes);

// One borrowed synchronous read. A lease grants no authority to publish a derived result.
public sealed class AttachmentReadLease : IDisposable
{
    private readonly object gate = new();
    private readonly byte[] bytes;
    private readonly AttachmentReadTracker.Slot slot;
    private bool running, finished, revoked;
    internal AttachmentReadLease(byte[] ownedBytes, AttachmentReadTracker.Slot slot)
    { bytes = ownedBytes; this.slot = slot; }
    public bool Consume(AttachmentByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (gate)
        {
            if (running || finished || revoked) throw new InvalidOperationException("Attachment read grant ended or was already consumed");
            running = true;
        }
        bool success = false;
        try { reader(bytes); }
        finally
        {
            lock (gate)
            {
                success = !revoked;
                running = false; finished = true;
                CryptographicOperations.ZeroMemory(bytes);
            }
            slot.Finish(); // No tracker lock is acquired while the lease monitor is held.
        }
        return success;
    }
    internal void Revoke()
    {
        bool cleanup = false;
        lock (gate)
        {
            revoked = true;
            if (!running && !finished)
            { finished = true; CryptographicOperations.ZeroMemory(bytes); cleanup = true; }
        }
        if (cleanup) slot.Finish();
    }
    public void Dispose() => Revoke();
}

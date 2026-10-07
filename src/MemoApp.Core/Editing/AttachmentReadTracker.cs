namespace MemoApp.Core.Editing;

// Cleanup-only object: no coordinator, vault, keys, worker Workspace reads, or UI callback.
internal sealed class AttachmentReadTracker
{
    private readonly object gate = new();
    private readonly HashSet<Slot> slots = [];
    private TaskCompletionSource? cycle;
    private readonly Action? beforeDrainCompletion;
    internal AttachmentReadTracker(Action? beforeDrainCompletion = null)
    { this.beforeDrainCompletion = beforeDrainCompletion; }
    internal Task WhenIdle { get { lock (gate) return cycle?.Task ?? Task.CompletedTask; } }
    internal Slot Reserve(object source)
    {
        lock (gate)
        {
            if (slots.Count >= 2) throw new InvalidOperationException("Attachment read capacity is occupied");
            if (slots.Count == 0) cycle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var slot = new Slot(this, source); slots.Add(slot); return slot;
        }
    }
    internal void Revoke(object? source = null)
    {
        AttachmentReadLease? first = null, second = null;
        lock (gate)
        {
            foreach (var slot in slots)
            {
                if (source is not null && !ReferenceEquals(source, slot.Source)) continue;
                slot.Revoked = true;
                if (first is null) first = slot.Lease; else second = slot.Lease;
            }
        }
        first?.Revoke(); second?.Revoke();
    }
    internal sealed class Slot
    {
        private readonly AttachmentReadTracker owner;
        internal object Source { get; }
        internal bool Revoked { get; set; }
        internal AttachmentReadLease? Lease { get; set; }
        internal Slot(AttachmentReadTracker owner, object source) { this.owner = owner; Source = source; }
        internal bool Bind(AttachmentReadLease lease)
        {
            lock (owner.gate)
            {
                if (Revoked || !owner.slots.Contains(this) || Lease is not null) return false;
                Lease = lease; return true;
            }
        }
        internal void Finish()
        {
            TaskCompletionSource? completed = null;
            lock (owner.gate)
            {
                if (!owner.slots.Remove(this)) return;
                Lease = null;
                if (owner.slots.Count == 0) completed = owner.cycle;
            }
            if (completed is not null)
            {
                try { owner.beforeDrainCompletion?.Invoke(); }
                finally { completed.TrySetResult(); }
            }
        }
    }
}

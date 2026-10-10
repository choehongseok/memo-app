using MemoApp.Core.Storage;
namespace MemoApp.Core.History;

// Analysis only: UUID order is presentation order, never causal authority.
internal static class RevisionBranchAnalysis
{
    internal static Guid[] PendingBranchTips(StoredNote current,IReadOnlyList<StoredRevision> history)
    {
        if(history.Count>512||history.Any(r=>r.NoteId!=current.NoteId))throw new InvalidDataException("Branch history bounds");
        var graph=history.ToDictionary(r=>r.RevisionId,r=>r.Parents);graph.Add(current.RevisionId,current.Parents);
        var ancestors=new HashSet<Guid>();var stack=new Stack<Guid>();stack.Push(current.RevisionId);
        while(stack.TryPop(out var id))if(ancestors.Add(id))foreach(var parent in graph[id])stack.Push(parent);
        var pending=history.Where(r=>!ancestors.Contains(r.RevisionId)).ToArray();
        var parents=pending.SelectMany(r=>r.Parents).ToHashSet();
        return pending.Where(r=>!parents.Contains(r.RevisionId)).Select(r=>r.RevisionId).Order().ToArray();
    }
}

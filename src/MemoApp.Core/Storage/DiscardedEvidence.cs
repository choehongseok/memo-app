namespace MemoApp.Core.Storage;
// Contentless immutable causal evidence; all loops/allocations have schema caps, including hidden-draft checks.
internal static class DiscardedEvidence
{
 internal static StoredDiscardedRevision[] Clone(StoredDiscardedRevision[] records)
 {
  if(records is null||records.Length>10100)throw new InvalidDataException("Discarded evidence bound");
  return records.Select(w=>w is not null&&w.Parents is {Length:<=8}?w with{Parents=(Guid[])w.Parents.Clone()}:throw new InvalidDataException("Discarded witness shape")).ToArray();
 }
 internal static StoredTombstone[] ContentlessMarkers(VaultSnapshot snapshot)
 {
  if(snapshot.Notes is null||snapshot.Notes.Length>100||snapshot.Tombstones is null||snapshot.Tombstones.Length>100)throw new InvalidDataException("Discarded marker bound");
  var ids=snapshot.Notes.Select(n=>n.NoteId).ToHashSet();return snapshot.Tombstones.Where(t=>!ids.Contains(t.NoteId)).Select(t=>t.Parents is {Length:<=8}?t with{Parents=(Guid[])t.Parents.Clone()}:throw new InvalidDataException("Discarded marker shape")).ToArray();
 }
 internal static void RequirePreserved(IEnumerable<StoredDiscardedRevision> knownWitnesses,IEnumerable<StoredTombstone> knownMarkers,VaultSnapshot snapshot)
 {
  if(snapshot.DiscardedRevisions is null||snapshot.DiscardedRevisions.Length>10100||snapshot.Notes is null||snapshot.Notes.Length>100||snapshot.Tombstones is null||snapshot.Tombstones.Length>100)throw new InvalidDataException("Discarded evidence candidate bound");
  var witnesses=snapshot.DiscardedRevisions.ToDictionary(w=>w.RevisionId);var markers=snapshot.Tombstones.ToDictionary(t=>t.NoteId);var current=snapshot.Notes.Select(n=>n.NoteId).ToHashSet();
  foreach(var known in knownWitnesses)
   if(!witnesses.TryGetValue(known.RevisionId,out var candidate)||candidate.NoteId!=known.NoteId||candidate.Parents is null||!candidate.Parents.SequenceEqual(known.Parents))throw new InvalidOperationException("Immutable discarded revision changed or disappeared");
  foreach(var known in knownMarkers)
   if(current.Contains(known.NoteId)||!markers.TryGetValue(known.NoteId,out var candidate)||candidate.RevisionId!=known.RevisionId||candidate.Parents is null||!candidate.Parents.SequenceEqual(known.Parents))throw new InvalidOperationException("Immutable discarded tombstone changed or resurrected");
 }
}

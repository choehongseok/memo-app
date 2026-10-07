using System.Collections.Immutable;
namespace MemoApp.Core.Storage;
// Ciphertext-only immutable record. Root/data keys never enter snapshots or UI models.
public sealed record StoredAttachmentObject(Guid ObjectId,Guid RootId,string Name,string Mime,int Length,string Sha256,string WrappedKey,ImmutableArray<string> Chunks);

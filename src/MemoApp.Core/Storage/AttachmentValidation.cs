using System.Collections.Immutable;
using System.Text.RegularExpressions;
using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
internal static class AttachmentValidation
{
    internal const int ChunkSize=65536,MaxObject=4*1024*1024,MaxTotal=8*1024*1024,MaxObjects=128,MaxReferences=16;
    internal static int ChunkCount(int length)=>Math.Max(1,(length+ChunkSize-1)/ChunkSize);
    internal static void Description(string name,string mime,int length,string hash)
    {
        if(name is null||name.Length is <1 or >256||name.Trim()!=name||name.Any(c=>char.IsControl(c)||c is '/' or '\\')||!RichDocumentCodec.IsWellFormedUnicode(name)||mime is null||mime.Length is <3 or >128||!Regex.IsMatch(mime,"\\A[a-z0-9][a-z0-9.+-]*/[a-z0-9][a-z0-9.+-]*\\z",RegexOptions.CultureInvariant)||length is <0 or >MaxObject||hash is null||!Regex.IsMatch(hash,"\\A[a-f0-9]{64}\\z",RegexOptions.CultureInvariant))throw new InvalidDataException("Attachment metadata");
    }
    internal static void Object(StoredAttachmentObject item,Guid root)
    {
        if(item is null||root==Guid.Empty||item.ObjectId==Guid.Empty||item.RootId!=root||item.Chunks.IsDefault)throw new InvalidDataException("Attachment object/root/chunks");
        Description(item.Name,item.Mime,item.Length,item.Sha256);
        if(item.Chunks.Length!=ChunkCount(item.Length))throw new InvalidDataException("Attachment chunk relation");
        Encoded(item.WrappedKey,60);foreach(var chunk in item.Chunks)Encoded(chunk,ChunkSize+16);
    }
    internal static void Encoded(string value,int size)
    {
        if(value is null||value.Length!=checked(((size+2)/3)*4)||!Regex.IsMatch(value,"\\A[A-Za-z0-9+/]*={0,2}\\z",RegexOptions.CultureInvariant))throw new InvalidDataException("Attachment encoded length/alphabet");
        byte[] decoded;try{decoded=Convert.FromBase64String(value);}catch(FormatException){throw new InvalidDataException("Attachment base64");}
        if(decoded.Length!=size||Convert.ToBase64String(decoded)!=value)throw new InvalidDataException("Noncanonical attachment base64");
    }
    internal static HashSet<Guid> Objects(VaultSnapshot snapshot)
    {
        if(snapshot.AttachmentObjects.IsDefault||snapshot.AttachmentObjects.Length>MaxObjects||snapshot.SchemaVersion>=5&&snapshot.AttachmentRootId==Guid.Empty||snapshot.SchemaVersion<5&&(snapshot.AttachmentRootId!=Guid.Empty||snapshot.AttachmentObjects.Length!=0))throw new InvalidDataException("Attachment root/count/schema");
        var ids=new HashSet<Guid>();long total=0;
        foreach(var item in snapshot.AttachmentObjects)
        {
            Object(item,snapshot.AttachmentRootId);if(!ids.Add(item.ObjectId))throw new InvalidDataException("Duplicate attachment object");
            total+=item.Length;if(total>MaxTotal)throw new InvalidDataException("Retained attachment byte budget");
        }
        return ids;
    }
    internal static void References(ImmutableArray<Guid> ids,HashSet<Guid> objects,int schema)
    {if(ids.IsDefault||ids.Length>MaxReferences||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!objects.Contains(id))||schema<5&&ids.Length!=0)throw new InvalidDataException("Attachment references/schema");}
    internal static bool SameObject(StoredAttachmentObject left,StoredAttachmentObject right)=>
        left.ObjectId==right.ObjectId&&left.RootId==right.RootId&&left.Name==right.Name&&left.Mime==right.Mime&&left.Length==right.Length&&left.Sha256==right.Sha256&&left.WrappedKey==right.WrappedKey&&left.Chunks.SequenceEqual(right.Chunks);
    internal static void Document(StyledDocument? document,int schema)
    {
        if(schema<5||document is null)return;
        // Image/opaque-reference interpretation is a later unit; never silently ignore its references.
        using var parsed=System.Text.Json.JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});
        var pending=new Stack<System.Text.Json.JsonElement>();pending.Push(parsed.RootElement);
        while(pending.TryPop(out var value))
        {
            if(value.ValueKind==System.Text.Json.JsonValueKind.Object)
                foreach(var property in value.EnumerateObject())
                {
                    if(property.Name is "attachmentId" or "attachmentIds"||property.Name=="type"&&property.Value.ValueKind==System.Text.Json.JsonValueKind.String&&string.Equals(property.Value.GetString(),"image",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Schema5 image/opaque attachment references are not integrated yet");
                    pending.Push(property.Value);
                }
            else if(value.ValueKind==System.Text.Json.JsonValueKind.Array)foreach(var child in value.EnumerateArray())pending.Push(child);
        }
    }
}

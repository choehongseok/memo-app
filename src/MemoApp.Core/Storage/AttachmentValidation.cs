using System.Collections.Immutable;
using System.Text.RegularExpressions;
using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
internal static class AttachmentValidation
{
    internal const int ChunkSize=65536,MaxObject=4*1024*1024,MaxTotal=8*1024*1024,MaxObjects=128,MaxReferences=16;
    internal static int ChunkCount(int length)=>Math.Max(1,(length+ChunkSize-1)/ChunkSize);
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
            if(item is null||item.ObjectId==Guid.Empty||!ids.Add(item.ObjectId)||item.RootId!=snapshot.AttachmentRootId||item.Name is null||item.Name.Length is <1 or >256||item.Name.Trim()!=item.Name||item.Name.Any(c=>char.IsControl(c)||c is '/' or '\\')||!RichDocumentCodec.IsWellFormedUnicode(item.Name)||item.Mime is null||item.Mime.Length is <3 or >128||!Regex.IsMatch(item.Mime,"\\A[a-z0-9][a-z0-9.+-]*/[a-z0-9][a-z0-9.+-]*\\z",RegexOptions.CultureInvariant)||item.Length is <0 or >MaxObject||item.Sha256 is null||!Regex.IsMatch(item.Sha256,"\\A[a-f0-9]{64}\\z",RegexOptions.CultureInvariant)||item.Chunks.IsDefault||item.Chunks.Length!=ChunkCount(item.Length))throw new InvalidDataException("Attachment metadata/chunk relation");
            total+=item.Length;if(total>MaxTotal)throw new InvalidDataException("Retained attachment byte budget");Encoded(item.WrappedKey,60);foreach(var chunk in item.Chunks)Encoded(chunk,ChunkSize+16);
        }
        return ids;
    }
    internal static void References(ImmutableArray<Guid> ids,HashSet<Guid> objects,int schema)
    {if(ids.IsDefault||ids.Length>MaxReferences||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!objects.Contains(id))||schema<5&&ids.Length!=0)throw new InvalidDataException("Attachment references/schema");}
}

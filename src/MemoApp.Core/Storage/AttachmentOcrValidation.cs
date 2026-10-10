using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Storage;
internal static class AttachmentOcrValidation
{
    internal const int MaxRecords=128,MaxTextBytes=1048576;
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static void Json(JsonElement root,int schema)
    {
        int count=0;long total=0;
        foreach(string collection in new[]{"notes","history"})foreach(var record in root.GetProperty(collection).EnumerateArray())
        {
            if(!record.TryGetProperty("metadata",out var metadata)||metadata.ValueKind!=JsonValueKind.Object){if(schema>=11)throw Refused();continue;}
            if(schema<11){if(metadata.TryGetProperty("attachmentOcrResults",out _))throw Refused();continue;}
            if(!metadata.TryGetProperty("attachmentOcrResults",out var results)||results.ValueKind!=JsonValueKind.Array||results.GetArrayLength()>16)throw Refused();
            foreach(var item in results.EnumerateArray())
            {
                if(++count>MaxRecords)throw Refused();Fields(item,["objectId","sourceSha256","text","textSha256","provenance"]);
                var text=item.GetProperty("text");if(text.ValueKind!=JsonValueKind.String)throw Refused();total=checked(total+TextBytes(text.GetString()!));if(total>MaxTextBytes)throw Refused();
                Fields(item.GetProperty("provenance"),["tesseractCommit","leptonicaCommit","modelsCommit","engineSha256","korModelSha256","engModelSha256","languages","oem","psm","transformProfile","sourceWidth","sourceHeight","previewWidth","previewHeight","ppmSha256"]);
            }
        }
    }
    private static void Fields(JsonElement value,string[] fields)
    {if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Count()!=fields.Length||fields.Any(f=>!value.TryGetProperty(f,out _)))throw Refused();}
    internal static void Snapshot(VaultSnapshot snapshot)
    {
        if(snapshot.Notes is null||snapshot.History is null)throw Refused();int count=0;long total=0;
        var objects=snapshot.AttachmentObjects.ToDictionary(o=>o.ObjectId);
        void Metadata(NoteMetadata metadata,ImmutableArray<Guid> references)
        {
            if(metadata is null||metadata.AttachmentOcrResults.IsDefault||metadata.AttachmentOcrResults.Length>16||metadata.AttachmentOcrResults.Length>0&&references.IsDefault||snapshot.SchemaVersion<11&&metadata.AttachmentOcrResults.Length!=0)throw Refused();
            var seen=new HashSet<Guid>();foreach(var item in metadata.AttachmentOcrResults)
            {
                if(++count>MaxRecords||item is null||!seen.Add(item.ObjectId)||!references.Contains(item.ObjectId)||!objects.TryGetValue(item.ObjectId,out var source)||source.RootId!=snapshot.AttachmentRootId||item.SourceSha256!=source.Sha256||!OcrProvenanceFacts.Hash(item.SourceSha256)||!OcrProvenanceFacts.Hash(item.TextSha256))throw Refused();
                int bytes=TextBytes(item.Text);total=checked(total+bytes);if(total>MaxTextBytes)throw Refused();
                byte[] encoded=Utf8.GetBytes(item.Text);try{if(Convert.ToHexStringLower(SHA256.HashData(encoded))!=item.TextSha256)throw Refused();}finally{CryptographicOperations.ZeroMemory(encoded);}
                Provenance(item.Provenance);
            }
        }
        foreach(var note in snapshot.Notes){if(note is null)throw Refused();Metadata(note.Metadata,note.AttachmentIds);}foreach(var revision in snapshot.History){if(revision is null)throw Refused();Metadata(revision.Metadata,revision.AttachmentIds);}
    }
    private static int TextBytes(string text)
    {if(text is null||text.Length is <1 or >65536||text.Contains('\0')||!RichDocumentCodec.IsWellFormedUnicode(text))throw Refused();int bytes=Utf8.GetByteCount(text);if(bytes>262144)throw Refused();return bytes;}
    private static void Provenance(StoredOcrProvenance p)
    {
        if(p is null||p.TesseractCommit!=OcrProvenanceFacts.TesseractCommit||p.LeptonicaCommit!=OcrProvenanceFacts.LeptonicaCommit||p.ModelsCommit!=OcrProvenanceFacts.ModelsCommit||p.Languages!=OcrProvenanceFacts.Languages||p.Oem!=OcrProvenanceFacts.Oem||p.Psm!=OcrProvenanceFacts.Psm||p.TransformProfile!=OcrProvenanceFacts.TransformProfile||p.KorModelSha256!="6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2"||p.EngModelSha256!="7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2")throw Refused();
        new OcrProvenanceFacts(p.EngineSha256,p.KorModelSha256,p.EngModelSha256,p.SourceWidth,p.SourceHeight,p.PreviewWidth,p.PreviewHeight,p.PpmSha256).Validate();
    }
    private static InvalidDataException Refused()=>new("Attachment OCR metadata authority or budget");
}

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace MemoApp.Core.Storage;
// Old payloads omit fields older clients do not understand; no change to their byte budget or envelope.
internal static class SnapshotSerialization
{
    private static readonly JsonSerializerOptions Legacy=CreateLegacy(false),AttachmentLegacy=CreateLegacy(true);
    private static JsonSerializerOptions CreateLegacy(bool attachments)
    {
        var resolver=new DefaultJsonTypeInfoResolver();resolver.Modifiers.Add(info=>
        {
            foreach(var property in info.Properties)
            {
                bool searchMetadata=info.Type==typeof(StoredDeviceUi)&&(property.Name is "recentNoteIds" or "savedSearches");
                bool attachmentMetadata=info.Type==typeof(VaultSnapshot)&&(property.Name is "attachmentRootId" or "attachmentObjects")||(info.Type==typeof(StoredNote)||info.Type==typeof(StoredRevision))&&property.Name=="attachmentIds";
                if(searchMetadata||!attachments&&attachmentMetadata)property.ShouldSerialize=(_,_)=>false;
            }
        });
        return new(VaultEnvelope.JsonOptions){TypeInfoResolver=resolver};
    }
    internal static JsonSerializerOptions Options(int schema)=>schema>=6?VaultEnvelope.JsonOptions:schema==5?AttachmentLegacy:Legacy;
    internal static byte[] Bytes(VaultSnapshot snapshot)=>JsonSerializer.SerializeToUtf8Bytes(snapshot,Options(snapshot.SchemaVersion));
    internal static void Write(Stream output,VaultSnapshot snapshot)=>JsonSerializer.Serialize(output,snapshot,Options(snapshot.SchemaVersion));
}

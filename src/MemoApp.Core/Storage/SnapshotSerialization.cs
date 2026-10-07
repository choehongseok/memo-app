using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace MemoApp.Core.Storage;
// Old payloads omit fields older clients do not understand; no change to their byte budget or envelope.
internal static class SnapshotSerialization
{
    private static readonly JsonSerializerOptions Legacy=CreateLegacy();
    private static JsonSerializerOptions CreateLegacy()
    {
        var resolver=new DefaultJsonTypeInfoResolver();resolver.Modifiers.Add(info=>
        {
            foreach(var property in info.Properties)
                if(info.Type==typeof(VaultSnapshot)&&property.Name is "attachmentRootId" or "attachmentObjects"||(info.Type==typeof(StoredNote)||info.Type==typeof(StoredRevision))&&property.Name=="attachmentIds")property.ShouldSerialize=(_,_)=>false;
        });
        return new(VaultEnvelope.JsonOptions){TypeInfoResolver=resolver};
    }
    internal static JsonSerializerOptions Options(int schema)=>schema>=5?VaultEnvelope.JsonOptions:Legacy;
    internal static byte[] Bytes(VaultSnapshot snapshot)=>JsonSerializer.SerializeToUtf8Bytes(snapshot,Options(snapshot.SchemaVersion));
    internal static void Write(Stream output,VaultSnapshot snapshot)=>JsonSerializer.Serialize(output,snapshot,Options(snapshot.SchemaVersion));
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
using System.Security.Cryptography;
internal static class DeviceSearchStateChecks
{
    internal static void Run()
    {
        var recent=typeof(StoredDeviceUi).GetProperty("RecentNoteIds")??throw new Exception("Bounded encrypted recent-note profile is missing");var saved=typeof(StoredDeviceUi).GetProperty("SavedSearches")??throw new Exception("Bounded encrypted saved-search profile is missing");
        foreach(int schema in new[]{1,2,3,4,5})
        {
            const string fixedProfile="""[{"uiDeviceId":"00000000-0000-0000-0000-000000000003","preferences":{"darkMode":false,"fontSize":14,"scale":1},"windows":[]}]""";
            string frozen="{\"schemaVersion\":"+schema+",\"deviceId\":\"00000000-0000-0000-0000-000000000001\",\"notes\":[],\"history\":[],\"tombstones\":[],\"folders\":[],\"tags\":[],\"uiDevices\":"+(schema>=3?fixedProfile:"[]")+(schema==5?",\"attachmentRootId\":\"00000000-0000-0000-0000-000000000002\",\"attachmentObjects\":[]":"")+"}";
            byte[] exact=System.Text.Encoding.UTF8.GetBytes(frozen);VaultChecks.Require(SnapshotSerialization.Bytes(VaultEnvelope.ReadSnapshot(exact)).SequenceEqual(exact),"Frozen original schema1-5 JSON bytes preserved: "+schema);
        }
        var profile=new StoredDeviceUi(Guid.NewGuid(),new(),[]);var legacy=new VaultSnapshot(5,Guid.NewGuid(),[]){AttachmentRootId=Guid.NewGuid(),UiDevices=[profile]};
        var node=JsonSerializer.SerializeToNode(legacy,VaultEnvelope.JsonOptions)!.AsObject();var device=node["uiDevices"]![0]!.AsObject();device.Remove("recentNoteIds");device.Remove("savedSearches");byte[] old=JsonSerializer.SerializeToUtf8Bytes(node,VaultEnvelope.JsonOptions);var roundtrip=VaultEnvelope.ReadSnapshot(old);VaultChecks.Require(old.SequenceEqual(SnapshotSerialization.Bytes(roundtrip)),"Schema5 exact original serializer bytes preserved with no new fields");
        var next=legacy with{SchemaVersion=6};byte[] bytes=SnapshotSerialization.Bytes(next);VaultChecks.Require(VaultEnvelope.ReadSnapshot(bytes).SchemaVersion==6,"Schema6 empty collections accepted and retained");
        foreach(int schema in new[]{1,2,3,4,5,6})
        {
            var candidate=new VaultSnapshot(schema,Guid.NewGuid(),[]){AttachmentRootId=schema>=5?Guid.NewGuid():Guid.Empty,Tombstones=Enumerable.Range(0,101).Select(_=>new StoredTombstone(Guid.NewGuid(),Guid.NewGuid(),[])).ToArray()};VaultEnvelope.Validate(candidate with{Tombstones=candidate.Tombstones.Take(100).ToArray()});VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(candidate),"Programmatic original100 tombstone cap preserved in every supported schema");
        }
        node=JsonSerializer.SerializeToNode(next,VaultEnvelope.JsonOptions)!.AsObject();device=node["uiDevices"]![0]!.AsObject();device.Remove("recentNoteIds");VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node)),"Schema6 requires explicit recent collection");
        node=JsonSerializer.SerializeToNode(next,VaultEnvelope.JsonOptions)!.AsObject();node["schemaVersion"]=5;VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node)),"Legacy schema carrying new profile fields rejected");
        node=JsonSerializer.SerializeToNode(next,VaultEnvelope.JsonOptions)!.AsObject();node["schemaVersion"]=7;VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node)),"Unknown future schema rejected");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(next with{UiDevices=[profile with{RecentNoteIds=default}]}),"Default/null recent collection rejected");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(next with{UiDevices=[profile with{RecentNoteIds=Enumerable.Repeat(Guid.NewGuid(),21).ToImmutableArray()}]}),"Recent capacity rejected");
        Guid noteId=Guid.NewGuid();var note=new StoredNote(noteId,Guid.NewGuid(),[],DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,"synthetic","body");var search=new StoredSavedSearch(Guid.NewGuid(),"합성 검색",new(){Query="문장 😀",Field=SearchField.Body,View=SearchView.Active,Sort=SearchSort.Modified,FolderId=Guid.NewGuid(),Tag="합성 태그",ModifiedFrom=DateTimeOffset.UnixEpoch,ModifiedUntil=DateTimeOffset.UnixEpoch.AddDays(1)});var complete=next with{Notes=[note],UiDevices=[profile with{RecentNoteIds=[noteId],SavedSearches=[search]}]};var restored=VaultEnvelope.ReadSnapshot(SnapshotSerialization.Bytes(complete));VaultChecks.Require(restored.UiDevices.Single().RecentNoteIds.SequenceEqual(new[]{noteId})&&restored.UiDevices.Single().SavedSearches.Single()==search,"Exact saved options including inert missing-folder reference preserved");
        void Invalid(StoredDeviceUi value,string message)=>VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(complete with{UiDevices=[value]}),message);
        var current=complete.UiDevices[0];Invalid(current with{RecentNoteIds=[noteId,noteId]},"Duplicate recent references");Invalid(current with{RecentNoteIds=[Guid.NewGuid()]},"Missing recent note reference");Invalid(current with{SavedSearches=default},"Default saved collection");Invalid(current with{SavedSearches=Enumerable.Range(0,21).Select(i=>search with{Id=Guid.NewGuid(),Name=$"saved{i}"}).ToImmutableArray()},"Saved capacity");Invalid(current with{SavedSearches=[search,search with{Id=Guid.NewGuid()}]},"Duplicate saved display name");
        foreach(var invalid in new[]{search with{Id=Guid.Empty},search with{Name=new string('n',65)},search with{Name="invalid\uD800"},search with{Options=new(){Query=new string('x',257)}},search with{Options=new(){Field=(SearchField)999}},search with{Options=new(){FolderId=Guid.Empty}},search with{Options=new(){FolderId=Guid.NewGuid(),UnfiledOnly=true}},search with{Options=new(){ModifiedFrom=DateTimeOffset.UnixEpoch.AddDays(2),ModifiedUntil=DateTimeOffset.UnixEpoch}}})Invalid(current with{SavedSearches=[invalid]},"Malformed saved identity/name/conditions");
        foreach(string required in new[]{"query","field","view","sort","folderId","unfiledOnly","includeDescendants","tag","favoriteOnly","importantOnly","modifiedFrom","modifiedUntil"})
        {var bad=JsonSerializer.SerializeToNode(complete,VaultEnvelope.JsonOptions)!;bad["uiDevices"]![0]!["savedSearches"]![0]!["options"]!.AsObject().Remove(required);VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(bad)),"Every saved option is required: "+required);}
        Console.WriteLine("PASS: structural schema6 encrypted UI metadata bounds and exact legacy schema5 serializer; metadata structural contract; encrypted migration checked separately");
    }
}

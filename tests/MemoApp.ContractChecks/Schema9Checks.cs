using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class Schema9Checks
{
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(StoredDeviceUi).GetProperty("AutomaticBackupPolicy") is not null,"Schema9 nullable encrypted opt-in backup policy is missing");
        string fixture=Path.Combine(Directory.GetCurrentDirectory(),"tests/fixtures/storage/frozen-schema8.json");
        byte[] frozen=File.ReadAllBytes(fixture);
        VaultChecks.Require(frozen.Length==2357&&Convert.ToHexStringLower(SHA256.HashData(frozen))=="96845312b47eca214aaf8a72fa5a41c650f18dc3be0c516791257375daf5925f","Actual pre-schema9 binary-written8 fixture exact hash");
        var eight=VaultEnvelope.ReadSnapshot(frozen);VaultChecks.Require(frozen.SequenceEqual(SnapshotSerialization.Bytes(eight)),"Frozen prechange8 exact bytes roundtrip");
        for(int schema=1;schema<=8;schema++)
        {
            var old=new VaultSnapshot(schema,eight.DeviceId,[]){AttachmentRootId=schema>=5?eight.AttachmentRootId:Guid.Empty,UiDevices=schema>=3?[new(eight.UiDevices[0].UiDeviceId,new(),[])]:[]};
            byte[] bytes=SnapshotSerialization.Bytes(old);VaultChecks.Require(!System.Text.Encoding.UTF8.GetString(bytes).Contains("automaticBackupPolicy",StringComparison.Ordinal),"Schema1..8 omit new field: "+schema);
            var bad=JsonNode.Parse(bytes)!;if(schema<3)bad["uiDevices"]=new JsonArray(JsonNode.Parse("{\"automaticBackupPolicy\":null}"));else bad["uiDevices"]![0]!["automaticBackupPolicy"]=null;Reject(bad,"Schema1..8 unknown field even null: "+schema);
        }
        var nine=eight with{SchemaVersion=9};
        byte[] off=SnapshotSerialization.Bytes(nine);VaultChecks.Require(VaultEnvelope.ReadSnapshot(off).SchemaVersion==9,"Schema9 default-off decryptable");
        var node=JsonNode.Parse(off)!;var device=node["uiDevices"]![0]!.AsObject();
        VaultChecks.Require(device.ContainsKey("automaticBackupPolicy")&&device["automaticBackupPolicy"] is null,"Schema9 explicit null default-off policy");
        device.Remove("automaticBackupPolicy");Reject(node,"Schema9 missing nullable policy field refused");
        node=JsonNode.Parse(frozen)!;node["uiDevices"]![0]!["automaticBackupPolicy"]=null;Reject(node,"Schema8 unknown policy even null refused");
        node=JsonNode.Parse(off)!;
        node["uiDevices"]![0]!["automaticBackupPolicy"]=JsonNode.Parse("""
        {"directory":"C:\\SYNTHETIC_BACKUP","vaultIdentity":"00000000-0000-0000-0000-000000000011","sourceRootBinding":{"rootPath":"C:\\SYNTHETIC_ROOT","volumeSerialNumber":18446744073709551615,"directoryFileId":"00112233445566778899aabbccddeeff"},"capacity":100,"afterSave":false,"daily":true,"onExit":true,"lastAttemptDay":"2026-10-09"}
        """);
        var valid=VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node));VaultChecks.Require(JsonNode.DeepEquals(JsonNode.Parse(SnapshotSerialization.Bytes(valid)),node),"Policy exact scalar DTO roundtrip without filesystem access");
        foreach(string field in new[]{"directory","vaultIdentity","sourceRootBinding","capacity","afterSave","daily","onExit","lastAttemptDay"})
        {var bad=node.DeepClone();bad["uiDevices"]![0]!["automaticBackupPolicy"]!.AsObject().Remove(field);Reject(bad,"Policy required field: "+field);}
        void Bad(string field,JsonNode? value,string reason){var bad=node.DeepClone();bad["uiDevices"]![0]!["automaticBackupPolicy"]![field]=value;Reject(bad,reason);}
        Bad("capacity",JsonValue.Create(0),"Capacity underflow");Bad("capacity",JsonValue.Create(101),"Capacity overflow");Bad("vaultIdentity",JsonValue.Create(Guid.Empty),"Empty vault identity");Bad("directory",JsonValue.Create(@"C:\..\backup"),"Unnormalized path");Bad("directory",JsonValue.Create(@"\\server\share"),"UNC refused");Bad("directory",JsonValue.Create(@"C:\backup:stream"),"ADS refused");VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(valid with{UiDevices=[valid.UiDevices[0] with{AutomaticBackupPolicy=valid.UiDevices[0].AutomaticBackupPolicy! with{Directory="C:\\backup\uD800"}}]}),"Ill-formed Unicode path direct DTO preflight");Bad("lastAttemptDay",JsonValue.Create("2026-10-09T00:00:00+01:00"),"Day must be strict UTC day");Bad("afterSave",JsonValue.Create(true),"After-save mutually exclusive with daily");var exitOnly=node.DeepClone();exitOnly["uiDevices"]![0]!["automaticBackupPolicy"]!["daily"]=false;VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(exitOnly));var noTrigger=exitOnly.DeepClone();noTrigger["uiDevices"]![0]!["automaticBackupPolicy"]!["onExit"]=false;Reject(noTrigger,"At least one trigger required");
        foreach(string field in new[]{"rootPath","volumeSerialNumber","directoryFileId"}){var bad=node.DeepClone();bad["uiDevices"]![0]!["automaticBackupPolicy"]!["sourceRootBinding"]!.AsObject().Remove(field);Reject(bad,"Binding required field: "+field);}
        foreach(var id in new[]{"","xyz",new string('0',32),new string('1',33)}){var bad=node.DeepClone();bad["uiDevices"]![0]!["automaticBackupPolicy"]!["sourceRootBinding"]!["directoryFileId"]=id;Reject(bad,"128-bit directory ID strict bounds");}
        var workspace=new EditingWorkspace(TimeProvider.System,nine);try
        {VaultChecks.Require(workspace.Capture().SchemaVersion==9,"Capture retains default-off schema9");VaultChecks.ExpectFailure(()=>workspace.AcceptPrepared(eight),"Workspace9 to8 downgrade refused");VaultChecks.Require(workspace.Capture().SchemaVersion==9,"Rejected downgrade leaves sticky9");}finally{workspace.Clear();}
        var afterSave=node.DeepClone();afterSave["uiDevices"]![0]!["automaticBackupPolicy"]!["daily"]=false;afterSave["uiDevices"]![0]!["automaticBackupPolicy"]!["afterSave"]=true;VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(afterSave));
        var dailyOnly=node.DeepClone();dailyOnly["uiDevices"]![0]!["automaticBackupPolicy"]!["onExit"]=false;VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(dailyOnly));
        var policy=valid.UiDevices[0].AutomaticBackupPolicy!;
        var legacyWorkspace=new EditingWorkspace(TimeProvider.System,eight);
        try{legacyWorkspace.SetAutomaticBackupPolicy(eight.UiDevices[0].UiDeviceId,policy);VaultChecks.Require(legacyWorkspace.Capture().SchemaVersion==9,"Explicit opt-in migrates9");legacyWorkspace.SetAutomaticBackupPolicy(eight.UiDevices[0].UiDeviceId,null);VaultChecks.Require(legacyWorkspace.Capture().SchemaVersion==9&&legacyWorkspace.GetUiDevice(eight.UiDevices[0].UiDeviceId).AutomaticBackupPolicy is null,"Disabling policy keeps sticky9 default-off");}finally{legacyWorkspace.Clear();}
        var deleted=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,"SYNTHETIC_PURGE","source"){Metadata=new(){Deleted=true}};
        var purgeBasis=new VaultSnapshot(9,eight.DeviceId,[deleted]){AttachmentRootId=eight.AttachmentRootId,Tombstones=[new(deleted.NoteId,deleted.RevisionId,[])],UiDevices=[new(eight.UiDevices[0].UiDeviceId,new(),[]){AutomaticBackupPolicy=policy}]};
        var purgeWorkspace=new EditingWorkspace(TimeProvider.System,purgeBasis);
        try{purgeWorkspace.PurgeTrash([deleted.NoteId],DateTimeOffset.UtcNow);var purged=purgeWorkspace.Capture();VaultChecks.Require(purged.SchemaVersion==9&&purged.UiDevices[0].AutomaticBackupPolicy==policy&&purged.DiscardedRevisions.Single().RevisionId==deleted.RevisionId,"D11 purge retains9 policy and original deletion witness");}finally{purgeWorkspace.Clear();}
        string root=Path.Combine(Path.GetTempPath(),"memo-schema9-"+Guid.NewGuid().ToString("N"));byte[] secret=RandomNumberGenerator.GetBytes(32);
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret)){var rooted=vault.InitializeAttachmentRoot(new VaultSnapshot(4,eight.DeviceId,[]));var save=nine with{AttachmentRootId=rooted.AttachmentRootId};var prepared=vault.Prepare(save);VaultChecks.ExpectFailure(()=>vault.Prepare(save with{SchemaVersion=8}),"Prepared9 before commit to8 refused");vault.Commit(prepared);VaultChecks.ExpectFailure(()=>vault.Prepare(save with{SchemaVersion=8}),"Loaded9 to8 refused");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(save with{SchemaVersion=8}),"Hidden9 to8 refused");}
            using(var reopened=EncryptedVault.Open(root,secret)){VaultChecks.Require(reopened.Loaded.SchemaVersion==9,"Actual encrypted9 restart default off");VaultChecks.ExpectFailure(()=>reopened.Prepare(reopened.Loaded with{SchemaVersion=8}),"Reopened loaded9 to8 refused");VaultChecks.ExpectFailure(()=>reopened.InitializeAttachmentRoot(reopened.Loaded with{SchemaVersion=8}),"Reopened root helper9 to8 refused");VaultChecks.ExpectFailure(()=>VaultEnvelope.Decrypt(Schema2Checks.Encode(JsonSerializer.SerializeToNode(nine,SnapshotSerialization.Options(9))!.AsObject(),secret),secret),"Envelope1/schema9 pairing refused");}
            VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(nine with{SchemaVersion=12}),"Future schema12 refused");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
        Console.WriteLine("PASS: schema9 opt-in policy, strict bounds, prechange8 exact bytes, prepared/loaded/hidden downgrade guards, D11 sticky9 and real encrypted restart");
        await Task.CompletedTask;
    }
    private static void Reject(JsonNode node,string message)=>VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node)),message);
}

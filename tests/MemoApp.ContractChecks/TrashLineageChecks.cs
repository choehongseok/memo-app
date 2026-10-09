using System.Reflection;
using System.Text.Json;
using MemoApp.Core.Storage;
namespace MemoApp.Core;
internal static class TrashLineageChecks
{
 internal static void Run()
 {
  Guid Stable(int i)=>Guid.Parse("00000000-0000-0000-0000-"+i.ToString("D12"));
  var oldNote=new StoredNote(Stable(4),Stable(5),[],DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,"SYNTHETIC_LEGACY","unchanged raw source");
  var oldSeven=new VaultSnapshot(7,Stable(1),[oldNote with{Metadata=new(){FilePathLinks=[new(Stable(6),Stable(3),"합성 자료.txt",@"C:\합성 폴더\합성 자료.txt")]}}]){AttachmentRootId=Stable(2),UiDevices=[new(Stable(3),new(),[])]};
  byte[] oldBytes=SnapshotSerialization.Bytes(oldSeven);VaultChecks.Require(oldBytes.Length==1089&&Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(oldBytes))=="ce304d83988eb81974788e4ba8fbb9d6a55b81f93bdeb8bab4d8d22a3456ff9b","Actual pre-schema8 source compiled independently: frozen nonempty schema7 bytes");
  VaultChecks.Require(SnapshotSerialization.Bytes(VaultEnvelope.ReadSnapshot(oldBytes)).SequenceEqual(oldBytes),"Frozen7 read/write exact unchanged");
  var type=typeof(VaultSnapshot).Assembly.GetType("MemoApp.Core.Storage.StoredDiscardedRevision");VaultChecks.Require(type is not null,"Schema8 contentless revision witness contract is missing");
  var property=typeof(VaultSnapshot).GetProperty("DiscardedRevisions");VaultChecks.Require(property is not null,"Schema8 bounded witness collection is missing");
  var note=Guid.NewGuid();var first=Guid.NewGuid();var deletion=Guid.NewGuid();var root=Guid.NewGuid();
  object Witness(Guid id,Guid[] parents)=>Activator.CreateInstance(type!,[note,id,parents])!;
  VaultSnapshot Make(params object[] witnesses)
  {var array=Array.CreateInstance(type!,witnesses.Length);for(int i=0;i<witnesses.Length;i++)array.SetValue(witnesses[i],i);var snapshot=new VaultSnapshot(8,Guid.NewGuid(),[]){AttachmentRootId=root,Tombstones=[new(note,deletion,[first])]};property!.SetValue(snapshot,array);return snapshot;}
  var valid=Make(Witness(first,[]),Witness(deletion,[first]));VaultEnvelope.Validate(valid);var bytes=SnapshotSerialization.Bytes(valid);var again=VaultEnvelope.ReadSnapshot(bytes);VaultChecks.Require(SnapshotSerialization.Bytes(again).SequenceEqual(bytes),"Schema8 contentless deletion DAG exact serialization roundtrip");
  foreach(int old in Enumerable.Range(1,7))VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(valid with{SchemaVersion=old}),"Older schema rejects typed contentless witnesses");
  var json=JsonSerializer.SerializeToNode(valid,VaultEnvelope.JsonOptions)!;json["schemaVersion"]=7;VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(json)),"Older JSON rejects witness fields even if present");
  VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(Witness(first,[]),Witness(deletion,[]))),"Tombstone original parent mismatch rejected");
  VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(Witness(first,[deletion]),Witness(deletion,[first]))),"Witness cycle rejected");
  VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(Witness(deletion,[first]))),"Missing historical witness rejected");
  VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(Witness(first,[]),Witness(first,[]),Witness(deletion,[first]))),"Duplicate historical identity rejected");
  var other=Activator.CreateInstance(type!,[Guid.NewGuid(),first,Array.Empty<Guid>()])!;VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(other,Witness(deletion,[first]))),"Cross-note witness parent rejected");
  var content=JsonSerializer.SerializeToNode(valid,SnapshotSerialization.Options(8))!;content["discardedRevisions"]![0]!["title"]="forbidden synthetic content";VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(content)),"Witness JSON cannot carry title/body/extra fields");
  var tooMany=Enumerable.Range(0,512).Select(_=>Witness(Guid.NewGuid(),[])).Prepend(Witness(deletion,[first])).Prepend(Witness(first,[])).ToArray();VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Make(tooMany)),"514 witnesses for one discarded note refused");VaultEnvelope.Validate(Make(tooMany.Take(513).ToArray()));
  VaultSnapshot Many(int total)
  {
   Guid[] ids=Enumerable.Range(0,100).Select(_=>Guid.NewGuid()).ToArray();Guid[] latest=Enumerable.Range(0,100).Select(_=>Guid.NewGuid()).ToArray();object[] records=Enumerable.Range(0,total).Select(i=>Activator.CreateInstance(type!,[ids[i%100],i<100?latest[i]:Guid.NewGuid(),Array.Empty<Guid>()])!).ToArray();
   var array=Array.CreateInstance(type!,records.Length);for(int i=0;i<records.Length;i++)array.SetValue(records[i],i);
   var many=new VaultSnapshot(8,Guid.NewGuid(),[]){AttachmentRootId=root,Tombstones=Enumerable.Range(0,100).Select(i=>new StoredTombstone(ids[i],latest[i],[])).ToArray()};property!.SetValue(many,array);return many;
  }
  VaultEnvelope.Validate(Many(10100));VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Many(10101)),"10101 combined revision witnesses refused");
  var marker=new StoredTombstone(Guid.NewGuid(),Guid.NewGuid(),[]);var legacy=oldSeven with{Tombstones=[marker]};var editor=new MemoApp.Core.Editing.EditingWorkspace(TimeProvider.System,legacy);
  VaultChecks.ExpectFailure(()=>editor.AcceptPrepared(legacy with{SchemaVersion=8,Tombstones=[]}),"First8 transition preserves legacy contentless marker");
  VaultChecks.ExpectFailure(()=>editor.AcceptPrepared(legacy with{SchemaVersion=8,Tombstones=[marker with{RevisionId=Guid.NewGuid()}]}),"First8 refuses old marker identity mutation");
  VaultChecks.ExpectFailure(()=>editor.AcceptPrepared(legacy with{SchemaVersion=8,Notes=legacy.Notes.Append(oldNote with{NoteId=marker.NoteId}).ToArray(),Tombstones=[]}),"First8 refuses old ID resurrection");
  VaultChecks.Require(editor.Capture().SchemaVersion==7,"Refused8 acceptance leaves previous schema and scalar state");
  var malformed=legacy with{SchemaVersion=8,DiscardedRevisions=[new(marker.NoteId,marker.RevisionId,new Guid[9])]};VaultChecks.ExpectFailure(()=>editor.AcceptPrepared(malformed),"Malformed8 preflight before sticky mutation");VaultChecks.Require(editor.Capture().SchemaVersion==7,"Malformed8 acceptance no partial activation");
  VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(valid with{SchemaVersion=10}),"Unknown future10 rejected");
  Console.WriteLine("PASS: schema8 contentless original deletion DAG, malformed/cross-note/cycle/legacy/future refusal; encryption activation verified separately");
 }
}

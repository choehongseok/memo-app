using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
internal static class Schema11OcrChecks
{
    internal static async Task Run()
    {
        byte[] frozen=File.ReadAllBytes("tests/fixtures/storage/frozen-schema10.json");VaultChecks.Require(frozen.Length==2912&&Convert.ToHexStringLower(SHA256.HashData(frozen))=="da2e67991474b41fc02315b44f63125305a172bf1a19ebf6b66e2e244ec9d28e","Actual pre11 schema10 fixture hash");var ten=VaultEnvelope.ReadSnapshot(frozen);VaultChecks.Require(SnapshotSerialization.Bytes(ten).SequenceEqual(frozen),"Exact frozen pre11 schema10 bytes");
        var eleven=ten with{SchemaVersion=11};try{VaultEnvelope.Validate(eleven);}catch(InvalidDataException e){throw new Exception("Schema11 empty note-scoped OCR payload is missing",e);}
        VaultChecks.Require(SnapshotSerialization.Bytes(eleven).AsSpan().IndexOf("attachmentOcrResults"u8)>=0,"Schema11 always serializes empty OCR arrays");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(eleven with{SchemaVersion=12}),"Future12 refused");
        foreach(int old in Enumerable.Range(1,10)){var node=JsonSerializer.SerializeToNode(ten,SnapshotSerialization.Options(10))!;node["schemaVersion"]=old;node["notes"]![0]!["metadata"]!["attachmentOcrResults"]=new JsonArray();VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(node)),"Old JSON refuses OCR field even empty");}
        string root=Path.Combine(Path.GetTempPath(),"memo-schema11-ocr-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[] original=[11,22,33,44];
        try
        {
            using(var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Title="synthetic source";note.Text="original body remains";VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Schema11 genuine anchored root");Guid id=session.AttachBytes(note,original,"synthetic.png","image/png",note.EditVersion);VaultChecks.Require(await session.SaveAsync(),"Original attachment saved");var other=session.Workspace.Duplicate(note);VaultChecks.Require(await session.SaveAsync(),"Shared object second note saved");
                var before=session.Workspace.Capture();var oldNote=before.Notes.Single(n=>n.NoteId==note.Id);string otherBefore=JsonSerializer.Serialize(before.Notes.Single(n=>n.NoteId==other.Id));
                object result=Result(before,id,"linked searchable OCR 한글\r\n");Apply(session.Workspace,note,result);var current=session.Workspace.Capture();VaultChecks.Require(current.SchemaVersion==11&&note.Title==oldNote.Title&&note.Text==oldNote.Text&&note.Mode==oldNote.Mode&&note.Document==oldNote.Document&&note.AttachmentIds.SequenceEqual(oldNote.AttachmentIds)&&current.AttachmentObjects.SequenceEqual(before.AttachmentObjects)&&JsonSerializer.Serialize(current.Notes.Single(n=>n.NoteId==other.Id))==otherBefore,"Only selected note metadata/revision changes; original body/other note/immutable object exact");
                Search(session.Workspace,note,other,true);VaultChecks.ExpectFailure(()=>Apply(session.Workspace,note,result),"Duplicate object OCR refuses whole apply");var linked=current.Notes.Single(n=>n.NoteId==note.Id);VaultChecks.Require(await session.SaveAsync(),"Schema11 exact encrypted save");
                var saved=session.Workspace.Capture();Budgets(saved,note.Id);PublicationGuards(saved,note.Id);Lineages(saved,note.Id);var corrupt=JsonSerializer.SerializeToNode(saved,SnapshotSerialization.Options(11))!;
                foreach(string property in new[]{"sourceSha256","textSha256"}){var bad=corrupt.DeepClone();bad["notes"]!.AsArray().First(n=>n!["noteId"]!.GetValue<string>()==note.Id.ToString())!["metadata"]!["attachmentOcrResults"]![0]![property]=new string('0',64);VaultChecks.ExpectFailure(()=>VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(bad)),"OCR source/text hash joins refused");}
                session.DetachAttachment(note,id,note.EditVersion);Search(session.Workspace,note,other,false);VaultChecks.Require(Records(note.Metadata).GetArrayLength()==0,"Detach removes active OCR but history remains");session.Workspace.RestoreRevision(note,linked.RevisionId);Search(session.Workspace,note,other,true);VaultChecks.Require(await session.SaveAsync(),"Restore exact OCR/reference revision saved");
                VaultChecks.Require(session.Workspace.Capture().SchemaVersion==11,"Schema11 remains sticky");
                VaultChecks.ExpectFailure(()=>session.Workspace.AcceptPrepared(session.Workspace.Capture() with{SchemaVersion=10}),"Workspace OCR sticky downgrade");
            }
            using(var vault=EncryptedVault.Open(root,secret)){var loaded=vault.Loaded;var hidden=loaded with{Notes=loaded.Notes.Select(n=>n with{Title=new string('x',257)}).ToArray()};vault.ValidateHiddenRoot(hidden);VaultChecks.ExpectFailure(()=>vault.Prepare(hidden),"Hidden title257 remains repairable instead of normally saveable");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(loaded with{SchemaVersion=10}),"Loaded11 hidden downgrade refuses even same object root");}
            using(var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System)){VaultChecks.Require(reopened.Workspace.Capture().SchemaVersion==11&&reopened.Workspace.Notes.Any(n=>Records(n.Metadata).GetArrayLength()==1),"Actual encrypted restart retains OCR");}
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
        StickyHidden();
        Console.WriteLine("PASS: schema11 OCR encrypted note-only metadata/search, exact legacy10, detach/restore and validation");
    }
    private static void StickyHidden()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-schema11-hidden-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var vault=EncryptedVault.Create(root,secret,secret);var rooted=vault.InitializeAttachmentRoot(vault.Loaded);var ten=rooted with{SchemaVersion=10};vault.Save(ten);var eleven=ten with{SchemaVersion=11};
            vault.ValidateHiddenRoot(eleven);VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(ten),"Hidden11 observation monotonically rejects hidden10 before prepare");VaultChecks.ExpectFailure(()=>vault.Prepare(ten),"Hidden11 observation monotonically rejects ordinary10");
            var prepared=vault.Prepare(eleven);VaultChecks.ExpectFailure(()=>vault.Prepare(ten),"Prepared11 empty OCR downgrade refused");vault.Commit(prepared);VaultChecks.ExpectFailure(()=>vault.Prepare(ten),"Loaded11 empty OCR downgrade refused");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static StoredRevision Revision(StoredNote n)=>new(n.NoteId,n.RevisionId,(Guid[])n.Parents.Clone(),n.ModifiedAt,n.Title,n.Text){Metadata=n.Metadata,Mode=n.Mode,Document=n.Document,AttachmentIds=n.AttachmentIds};
    private static void Lineages(VaultSnapshot source,Guid noteId)
    {
        var selected=source.Notes.Single(n=>n.NoteId==noteId);var w=new EditingWorkspace(TimeProvider.System,source);
        try
        {
            var note=w.Notes.Single(n=>n.Id==noteId);var copy=w.Duplicate(note);VaultChecks.Require(copy.Metadata.AttachmentOcrResults.SequenceEqual(note.Metadata.AttachmentOcrResults)&&copy.AttachmentIds.SequenceEqual(note.AttachmentIds),"Selected copy keeps exact historical OCR and object reference");
            w.DeleteNotes([note]);VaultChecks.Require(NoteSearch.Find(w,new(){Query="searchable",Field=SearchField.Attachments,View=SearchView.Trash}).Select(n=>n.Id).SequenceEqual([note.Id]),"Trash OCR searches only current referencing trash note");w.RestoreNote(note);VaultChecks.Require(note.Metadata.AttachmentOcrResults.SequenceEqual(selected.Metadata.AttachmentOcrResults),"Trash restore exact OCR metadata");w.DeleteNotes([note]);var objects=w.Capture().AttachmentObjects;w.PurgeTrash([note.Id],DateTimeOffset.UtcNow);VaultChecks.Require(w.Capture().SchemaVersion==11&&w.Capture().AttachmentObjects.SequenceEqual(objects)&&w.Capture().Notes.All(n=>n.NoteId!=noteId)&&w.Capture().History.All(n=>n.NoteId!=noteId),"D11 clears note-derived payload lineage but retained immutable object storage remains");
        }
        finally{w.Clear();}
        NoteMetadata Strip(NoteMetadata m)=>m with{AttachmentOcrResults=[]};var legacy=source with{SchemaVersion=10,Notes=source.Notes.Select(n=>n with{Metadata=Strip(n.Metadata)}).ToArray(),History=source.History.Select(r=>r with{Metadata=Strip(r.Metadata)}).ToArray()};
        var import=new EditingWorkspace(TimeProvider.System,legacy);try{var copies=import.ImportBackupNotes(source,[noteId],_=>{});VaultChecks.Require(import.Capture().SchemaVersion==11&&copies.Single().Metadata.AttachmentOcrResults.SequenceEqual(selected.Metadata.AttachmentOcrResults)&&NoteSearch.Find(import,new(){Query="searchable",Field=SearchField.Attachments}).Select(n=>n.Id).SequenceEqual([copies.Single().Id]),"Selected backup copy promotes11 without changing original note search scope");}finally{import.Clear();}
        var merge=new EditingWorkspace(TimeProvider.System,legacy);
        try
        {
            var ancestor=legacy.Notes.Single(n=>n.NoteId==noteId);var branch=selected with{RevisionId=Guid.NewGuid(),Parents=[ancestor.RevisionId],Title="incoming OCR branch"};var backup=source with{Notes=source.Notes.Select(n=>n.NoteId==noteId?branch:n).ToArray(),History=legacy.History.Append(Revision(ancestor)).ToArray()};
            var prepared=merge.PrepareBackupMerge(backup,[noteId],_=>{});merge.ApplyPreparedBackupMerge(prepared,()=>true,()=>{});var after=merge.Capture();VaultChecks.Require(after.SchemaVersion==11&&after.Notes.Single(n=>n.NoteId==noteId).RevisionId==ancestor.RevisionId&&after.Notes.Single(n=>n.NoteId==noteId).Metadata.AttachmentOcrResults.IsEmpty&&after.History.Single(r=>r.RevisionId==branch.RevisionId).Metadata.AttachmentOcrResults.SequenceEqual(selected.Metadata.AttachmentOcrResults)&&merge.PendingBackupBranchTips(merge.Notes.Single(n=>n.Id==noteId)).SequenceEqual([branch.RevisionId]),"O06 OCR branch promotes11 while current head stays unmodified and pending");VaultChecks.Require(NoteSearch.Find(merge,new(){Query="searchable",Field=SearchField.Attachments}).Length==0,"Pending historical OCR does not leak into current search");
            var changed=backup with{History=backup.History.Select(r=>r.RevisionId==ancestor.RevisionId?r with{Metadata=selected.Metadata}:r).ToArray()};VaultChecks.ExpectFailure(()=>merge.PrepareBackupMerge(changed,[noteId],_=>{}),"O06 immutable revision conflict detects OCR-only metadata difference");
            var detached=VaultEnvelope.ReadSnapshot(SnapshotSerialization.Bytes(after));var same=merge.PrepareBackupMerge(detached,[noteId],_=>{});merge.ApplyPreparedBackupMerge(same,()=>true,()=>{});VaultChecks.Require(SnapshotSerialization.Bytes(after).SequenceEqual(SnapshotSerialization.Bytes(merge.Capture())),"O06 structural OCR equality survives independently deserialized arrays");
        }
        finally{merge.Clear();}
    }
    private static StoredAttachmentOcrResult WithText(StoredAttachmentOcrResult r,string text)=>r with{Text=text,TextSha256=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))};
    private static void Budgets(VaultSnapshot snapshot,Guid noteId)
    {
        var source=snapshot.Notes.Single(n=>n.NoteId==noteId);var result=source.Metadata.AttachmentOcrResults.Single();
        VaultSnapshot Only(StoredAttachmentOcrResult r)=>snapshot with{Notes=[source with{Parents=[],Metadata=source.Metadata with{AttachmentOcrResults=[r]}}],History=[],Tombstones=[]};
        foreach(string text in new[]{"","a\0b",new string('x',65537),"\ud800"})VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Only(WithText(result,text))),"Malformed/empty/NUL/UTF16 OCR text bounded");
        VaultEnvelope.Validate(Only(WithText(result,new string('x',65536))));
        string escaped=new string('\u0001',65536);var escapedNotes=Enumerable.Range(0,44).Select(_=>source with{NoteId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Parents=[],Text=escaped}).ToArray();var escapedCandidate=snapshot with{Notes=snapshot.Notes.Concat(escapedNotes).ToArray()};VaultEnvelope.Validate(escapedCandidate with{Notes=escapedCandidate.Notes.Select(n=>n with{Text="small"}).ToArray()});bool payloadRefused=false;try{VaultEnvelope.Validate(escapedCandidate);}catch(InvalidDataException e)when(e.Message.Contains("payload byte budget",StringComparison.Ordinal)){payloadRefused=true;}VaultChecks.Require(payloadRefused,"Otherwise-valid escaped16MiB payload specifically refuses byte budget");
        var duplicate=source with{Metadata=source.Metadata with{AttachmentOcrResults=[result,result]}};VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(snapshot with{Notes=[duplicate],History=[]}),"Duplicate per-object result rejected");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Only(result with{ObjectId=Guid.NewGuid()})),"Missing immutable reference rejects");
        foreach(var provenance in new[]{result.Provenance with{Languages="eng"},result.Provenance with{Psm=7},result.Provenance with{PreviewWidth=2},result.Provenance with{EngineSha256="bad"},result.Provenance with{KorModelSha256=new string('0',64)}})VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(Only(result with{Provenance=provenance})),"Closed historical profile rejects");
        var many=Enumerable.Range(0,129).Select(_=>new StoredRevision(source.NoteId,Guid.NewGuid(),[],source.ModifiedAt,source.Title,source.Text){Metadata=source.Metadata,AttachmentIds=source.AttachmentIds}).ToArray();
        var maximum=Only(result) with{History=many.Take(127).ToArray()};AttachmentOcrValidation.Snapshot(maximum);VaultChecks.ExpectFailure(()=>AttachmentOcrValidation.Snapshot(maximum with{History=many.Take(128).ToArray()}),"Current plus history occurrence128 cap");
        var large=WithText(result,new string('한',65536));var largeRevision=many[0] with{Metadata=source.Metadata with{AttachmentOcrResults=[large]}};VaultChecks.ExpectFailure(()=>AttachmentOcrValidation.Snapshot(Only(large) with{History=Enumerable.Range(0,5).Select(_=>largeRevision with{RevisionId=Guid.NewGuid()}).ToArray()}),"Aggregate UTF8 one MiB history cap");
        var workspace=new EditingWorkspace(TimeProvider.System,maximum);try{var draft=workspace.Notes.Single();long version=draft.EditVersion;VaultChecks.ExpectFailure(()=>draft.Title="budget edit","Later ordinary title edit cannot exceed history OCR cap");VaultChecks.Require(draft.EditVersion==version&&draft.Title==source.Title,"Budget rejection precedes any draft field mutation");}finally{workspace.Clear();}
    }
    private static void PublicationGuards(VaultSnapshot snapshot,Guid noteId)
    {
        var source=snapshot.Notes.Single(n=>n.NoteId==noteId);var result=source.Metadata.AttachmentOcrResults.Single();var clean=snapshot with{Notes=snapshot.Notes.Select(n=>n.NoteId==noteId?n with{Metadata=n.Metadata with{AttachmentOcrResults=[]}}:n).ToArray()};
        foreach(bool throwing in new[]{false,true})
        {
            var workspace=new EditingWorkspace(TimeProvider.System,clean);try{var note=workspace.Notes.Single(n=>n.Id==noteId);var before=SnapshotSerialization.Bytes(workspace.Capture());bool authority=true;workspace.AttachmentReadInvalidating+=_=>{authority=false;if(throwing)throw new InvalidOperationException("observer failed");};VaultChecks.ExpectFailure(()=>workspace.ApplyAttachmentOcrResult(note,result,()=>authority),"Invalidation failure/stale authority refuses publication");VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(workspace.Capture())),"Pre-publication refusal preserves whole candidate");}finally{workspace.Clear();}
        }
        foreach(int mutation in Enumerable.Range(0,4))
        {
            var workspace=new EditingWorkspace(TimeProvider.System,clean);try{var note=workspace.Notes.Single(n=>n.Id==noteId);bool changed=false,marked=false;bool Current(){if(!changed){changed=true;if(mutation==0)note.Title="callback changed";else if(mutation==1)workspace.AcceptPrepared(clean);else if(mutation==2)workspace.CreateNote();else workspace.Clear();}return true;}VaultChecks.ExpectFailure(()=>workspace.ApplyAttachmentOcrResult(note,result,Current,()=>marked=true),"Current callback mutation caught by after pure recheck");VaultChecks.Require(!marked&&note.Metadata.AttachmentOcrResults.IsEmpty,"Reentrant authority callback never publishes OCR");}finally{workspace.Clear();}
        }
        var applied=new EditingWorkspace(TimeProvider.System,clean);try{bool marked=false;applied.Changed+=()=>throw new InvalidOperationException("after publication");var note=applied.Notes.Single(n=>n.Id==noteId);VaultChecks.ExpectFailure(()=>applied.ApplyAttachmentOcrResult(note,result,null,()=>marked=true),"Post publication observer error");VaultChecks.Require(marked&&note.Metadata.AttachmentOcrResults.Length==1,"Applied state marked before failing observer");}finally{applied.Clear();}
    }
    private static JsonElement Records(NoteMetadata metadata)=>JsonSerializer.SerializeToElement(metadata,SnapshotSerialization.Options(11)).GetProperty("attachmentOcrResults");
    private static object Result(VaultSnapshot snapshot,Guid id,string text)
    {
        var json=JsonSerializer.SerializeToNode(snapshot with{SchemaVersion=11},SnapshotSerialization.Options(11))!;var note=json["notes"]!.AsArray().First(n=>n!["attachmentIds"]!.AsArray().Any(x=>x!.GetValue<string>()==id.ToString()))!;
        var provenance=new JsonObject{["tesseractCommit"]="db0ec62f81b0737fbbe184d8fea40af5738f8eef",["leptonicaCommit"]="13275a278eb55b5746e33f95fbf5a2c8f604b3ab",["modelsCommit"]="87416418657359cb625c412a48b6e1d6d41c29bd",["engineSha256"]=new string('a',64),["korModelSha256"]="6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2",["engModelSha256"]="7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2",["languages"]="kor+eng",["oem"]=1,["psm"]=6,["transformProfile"]="png-nearest-1024-straight-alpha-white-ppm-v1",["sourceWidth"]=1,["sourceHeight"]=1,["previewWidth"]=1,["previewHeight"]=1,["ppmSha256"]=new string('b',64)};
        note["metadata"]!["attachmentOcrResults"]=new JsonArray(new JsonObject{["objectId"]=id,["sourceSha256"]=snapshot.AttachmentObjects.Single(o=>o.ObjectId==id).Sha256,["text"]=text,["textSha256"]=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),["provenance"]=provenance});
        var loaded=VaultEnvelope.ReadSnapshot(JsonSerializer.SerializeToUtf8Bytes(json));return ((System.Collections.IEnumerable)typeof(NoteMetadata).GetProperty("AttachmentOcrResults")!.GetValue(loaded.Notes.First(n=>n.AttachmentIds.Contains(id)).Metadata)!).Cast<object>().Single();
    }
    private static void Apply(EditingWorkspace workspace,NoteDraft note,object result){try{typeof(EditingWorkspace).GetMethod("ApplyAttachmentOcrResult",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(workspace,[note,result,null,null]);}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();}}
    private static void Search(EditingWorkspace workspace,NoteDraft source,NoteDraft other,bool linked)
    {foreach(var field in new[]{SearchField.Attachments,SearchField.All})VaultChecks.Require(NoteSearch.Find(workspace,new(){Query="searchable",Field=field}).Select(n=>n.Id).SequenceEqual(linked?new[]{source.Id}:Array.Empty<Guid>()),"OCR search is note-scoped and excludes shared-object other note");foreach(var field in new[]{SearchField.Body,SearchField.Title})VaultChecks.Require(NoteSearch.Find(workspace,new(){Query="searchable",Field=field}).Length==0,"Body/title search unaffected");}
}

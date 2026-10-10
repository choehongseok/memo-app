using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class InlineImageChecks
{
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(RichDocumentCodec).GetMethod("Images") is not null,"H01 canonical v2 image extraction API is missing");
        VaultChecks.Require(typeof(SaveCoordinator).GetMethod("InsertInlineImageAsync") is not null,"H01 explicit authenticated insertion API is missing");
        Guid id=Guid.NewGuid(),root=Guid.NewGuid(),device=Guid.NewGuid();string label="합성.png";
        string original=" { \"nodes\" : [ {\"type\":\"paragraph\",\"runs\":[{\"text\":\"A\\u0020B\",\"bold\":true}]}, {\"type\":\"paragraph\",\"runs\":[{\"text\":\"end\"}]} ] } ";
        var one=new StyledDocument(1,original);var two=RichDocumentImageEdit.Insert(one,1,id,label);
        VaultChecks.Require(two.SchemaVersion==2&&RichDocumentCodec.Inspect(two).Text=="A B\n[이미지: 합성.png]\nend","Image exact inter-block text projection");
        VaultChecks.Require(RichDocumentCodec.Images(one).Length==0&&RichDocumentCodec.Images(two).Single()==new StoredInlineImage(1,id,label),"Versioned image extraction has block index and no v1 authority");
        var repeated=RichDocumentImageEdit.Insert(RichDocumentImageEdit.Insert(two,0,id,label),4,id,label);VaultChecks.Require(RichDocumentCodec.Images(repeated).Length==3,"First last repeated image references");
        var removed=RichDocumentImageEdit.Remove(two,1);VaultChecks.Require(removed.SchemaVersion==2&&RichDocumentCodec.Images(removed).Length==0&&RichDocumentCodec.Inspect(removed).Text=="A B\nend","Removing last image keeps document2 and text");
        using(var parsed=JsonDocument.Parse(two.SourceJson))using(var old=JsonDocument.Parse(original))VaultChecks.Require(parsed.RootElement.GetProperty("nodes")[0].GetRawText()==old.RootElement.GetProperty("nodes")[0].GetRawText(),"Untouched block raw whitespace/escapes retained");
        string Image(string fields)=>"{\"nodes\":[{"+fields+"}]}";
        foreach(string fields in new[]{"\"type\":\"image\",\"attachmentId\":\""+id.ToString("N")+"\",\"alt\":\"a\"","\"type\":\"image\",\"attachmentId\":\""+Guid.Empty+"\",\"alt\":\"a\"","\"type\":\"image\",\"attachmentId\":\""+id+"\"","\"type\":\"image\",\"attachmentId\":\""+id+"\",\"alt\":\"a\",\"url\":\"https://example.invalid\"","\"type\":\"image\",\"attachmentId\":\""+id+"\",\"alt\":\"a\",\"alt\":\"b\"","\"type\":\"future\",\"runs\":[]"})VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(new(2,Image(fields))),"Strict image/v2 fields and canonical identity");
        foreach(string alt in new[]{"", " padded ","line\n",new string('a',257),"\uD800"})VaultChecks.ExpectFailure(()=>RichDocumentImageEdit.Insert(one,0,id,alt),"Alt strict scalar bounds");
        foreach(string nested in new[]{"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"a\",\"attachmentId\":\""+id+"\"}]}]}","{\"nodes\":[{\"type\":\"list\",\"ordered\":false,\"items\":[{\"runs\":[],\"type\":\"image\",\"attachmentId\":\""+id+"\",\"alt\":\"a\"}]}]}","{\"nodes\":[{\"type\":\"table\",\"rows\":[[{\"runs\":[],\"attachmentIds\":[]}]]}]}"})VaultChecks.ExpectFailure(()=>RichDocumentCodec.Inspect(new(2,nested)),"Image authority cannot appear in nested run/list/table fields");
        var opaque=new StyledDocument(1," {\"nodes\":[{\"type\":\"image\",\"attachmentId\":\""+id+"\",\"alt\":\"\\uD569\\uC131\",\"future\":1.00e+2}] } ");VaultChecks.Require(!RichDocumentCodec.Inspect(opaque).Supported&&RichDocumentCodec.Images(opaque).Length==0,"V1 opaque image-looking source has zero image authority");VaultChecks.ExpectFailure(()=>RichDocumentImageEdit.Insert(opaque,0,id,label),"Opaquev1 cannot be implicitly edited/upgraded");
        var fullText=RichDocumentCodec.FromPlain(new string('x',RichDocumentCodec.MaxText));VaultChecks.ExpectFailure(()=>RichDocumentImageEdit.Insert(fullText,1,id,"a"),"Image projection respects existing whole-text limit");
        var maxNodes=new StyledDocument(1,"{\"nodes\":["+string.Join(',',Enumerable.Repeat("{\"type\":\"paragraph\",\"runs\":[]}",1023))+"]}");var atLimit=RichDocumentImageEdit.Insert(maxNodes,0,id,"a");VaultChecks.Require(RichDocumentCodec.Images(atLimit).Length==1,"Image counts toward exact existing 1024-node budget");VaultChecks.ExpectFailure(()=>RichDocumentImageEdit.Insert(atLimit,0,id,"a"),"Image cannot expand existing node budget");
        var item=Object(id,root);var note=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,"synthetic",RichDocumentCodec.Inspect(two).Text!,"rich"){Document=two,AttachmentIds=[id]};
        var ten=new VaultSnapshot(10,device,[note]){AttachmentRootId=root,AttachmentObjects=[item]};VaultEnvelope.Validate(ten);
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(ten with{SchemaVersion=9}),"Document2 requires payload10 even with authenticated object");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(ten with{Notes=[note with{AttachmentIds=[]}]}),"Image cannot borrow another record/global object reference");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(ten with{AttachmentObjects=[item with{Mime="application/octet-stream"}]}),"Only authenticated PNG MIME grants image attempt");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(ten with{AttachmentObjects=[item with{RootId=Guid.NewGuid()}]}),"Foreign attachment root cannot grant image authority");
        var historical=new StoredRevision(note.NoteId,Guid.NewGuid(),[],DateTimeOffset.UnixEpoch,"old",note.Text){Mode="rich",Document=two,AttachmentIds=[]};VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(ten with{History=[historical]}),"History image needs its own attachment IDs");
        var inert=ten with{Notes=[note with{Document=opaque,Text="AUTHENTICATED_OPAQUE_TEXT",AttachmentIds=[]}]};VaultEnvelope.Validate(inert);var opaqueHistory=new StoredRevision(note.NoteId,Guid.NewGuid(),[],DateTimeOffset.UnixEpoch,"opaque original","AUTHENTICATED_HISTORY_TEXT"){Mode="rich",Document=opaque};inert=inert with{History=[opaqueHistory]};VaultEnvelope.Validate(inert);var inertRoundtrip=VaultEnvelope.ReadSnapshot(SnapshotSerialization.Bytes(inert));VaultChecks.Require(inertRoundtrip.Notes[0].Document!.SourceJson==opaque.SourceJson&&inertRoundtrip.Notes[0].Text=="AUTHENTICATED_OPAQUE_TEXT"&&inertRoundtrip.History.Single().Document!.SourceJson==opaque.SourceJson&&inertRoundtrip.History.Single().Text=="AUTHENTICATED_HISTORY_TEXT","Schema10 opaquev1 exact source/authenticated Text survive without inferred references");
        VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(inert with{SchemaVersion=9}),"Schema9 old opaque-reference rejection retained");
        var basis=ten with{SchemaVersion=9,Notes=[note with{Document=one,Text=RichDocumentCodec.Inspect(one).Text!}]};var workspace=new EditingWorkspace(TimeProvider.System,basis);
        try
        {
            var draft=workspace.Notes.Single();workspace.InsertInlineImage(draft,id,1,label);VaultChecks.Require(workspace.Capture().SchemaVersion==10&&draft.Document==two&&workspace.HistoryFor(draft).Single().Document!.SourceJson==original,"Atomic explicit image event raises10 and retains complete exact v1 predecessor");
            byte[] before=SnapshotSerialization.Bytes(workspace.Capture());VaultChecks.ExpectFailure(()=>workspace.DetachAttachment(draft,id),"Active image prevents independent attachment detach");VaultChecks.ExpectFailure(()=>workspace.SetRichDocument(draft,one),"Native rich replacement cannot drop document2");VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(workspace.Capture())),"Refused native edit/detach leave complete state exact");
            var duplicate=workspace.Duplicate(draft);VaultChecks.Require(duplicate.Document==two&&duplicate.AttachmentIds.SequenceEqual(draft.AttachmentIds),"Clone retains exact placement/reference authority");
            workspace.RemoveInlineImage(draft,1);var empty=workspace.Capture();VaultChecks.Require(empty.SchemaVersion==10&&draft.Document!.SchemaVersion==2&&draft.AttachmentIds.Contains(id)&&empty.AttachmentObjects.Single()==item,"Image removal is non-GC and retains independent attachment reference");
            workspace.RestoreRevision(draft,workspace.HistoryFor(draft).Last().RevisionId);VaultChecks.Require(draft.Document==one&&workspace.Capture().SchemaVersion==10,"Restoring originalv1 stays payload10");
            var profile=Guid.NewGuid();var policy=new StoredAutomaticBackupPolicy(@"C:\SYNTHETIC_BACKUP",Guid.NewGuid(),new(@"C:\SYNTHETIC_ROOT",1,"00112233445566778899aabbccddeeff"),4,true,false,true);workspace.SetAutomaticBackupPolicy(profile,policy);workspace.SetAutomaticBackupPolicy(profile,null);VaultChecks.Require(workspace.Capture().SchemaVersion==10,"Policy enable/disable cannot lower10");
            VaultChecks.ExpectFailure(()=>workspace.AcceptPrepared(workspace.Capture() with{SchemaVersion=9}),"Workspace10 downgrade refused even after v1 restore and policy disable");
        }
        finally{workspace.Clear();}
        var importWorkspace=new EditingWorkspace(TimeProvider.System,basis with{Notes=[],AttachmentObjects=[]});try{var imported=importWorkspace.ImportBackupNotes(ten,[note.NoteId],_=>{}).Single();VaultChecks.Require(importWorkspace.Capture().SchemaVersion==10&&imported.Document==two,"Selected import promotes candidate10 before validation and preserves source");}finally{importWorkspace.Clear();}
        Console.WriteLine("PASS: H01 strict v2 PNG blocks, record/history/root authority, opaquev1 inert preservation, atomic history/clone/non-GC removal/import and policy sticky10");
        await Task.CompletedTask;
    }
    private static StoredAttachmentObject Object(Guid id,Guid root)=>new(id,root,"합성.png","image/png",0,new string('0',64),Convert.ToBase64String(new byte[60]),[Convert.ToBase64String(new byte[AttachmentValidation.ChunkSize+16])]);
}

using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
internal static class RichStorageChecks
{
    private static void RichMutationLimits()
    {
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var doc=RichDocumentCodec.FromPlain("bounded rich");
        var current=new StoredNote(id,Guid.NewGuid(),[],now,now,"bounded","bounded rich","rich"){Document=doc};
        foreach(var count in new[]{512,253})
        {
            var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[current]){History=Enumerable.Range(0,count).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"old",count==512?"bounded":new string('x',65536))).ToArray()};VaultEnvelope.Validate(snapshot);
            var bounded=new EditingWorkspace(TimeProvider.System,snapshot);var draft=bounded.Notes.Single();string before=JsonSerializer.Serialize(bounded.Capture()),basis=JsonSerializer.Serialize(bounded.FrozenBasis);int events=0;bounded.Changed+=()=>events++;long version=draft.EditVersion;
            var replacement=count==512?new StyledDocument(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"bounded rich\",\"bold\":true}]}]}"):RichDocumentCodec.FromPlain(new string('r',65536));
            VaultChecks.ExpectFailure(()=>bounded.SetRichDocument(draft,replacement),"rich history/whole-byte budget refusal");
            VaultChecks.Require(events==0 && version==draft.EditVersion && JsonSerializer.Serialize(bounded.Capture())==before && JsonSerializer.Serialize(bounded.FrozenBasis)==basis && draft.Document==doc,"rejected rich mutation preserves full draft/basis/events/version");bounded.Clear();
        }
    }
    internal static async Task Run()
    {
        var names=new EditingWorkspace(TimeProvider.System);var named=names.CreateNote();names.AcceptPrepared(names.Capture());string namesBefore=JsonSerializer.Serialize(names.Capture());int nameEvents=0;names.Changed+=()=>nameEvents++;
        VaultChecks.ExpectFailure(()=>names.CreateFolder("bad\uD800"),"invalid Unicode folder rejected before mutation");
        VaultChecks.ExpectFailure(()=>names.SetTags(named,["bad\uD800"]),"invalid Unicode tag rejected before mutation");
        var profile=Guid.NewGuid();VaultChecks.ExpectFailure(()=>names.SetWindowLayout(profile,new("memo",named.Id,"bad\uD800",0,0,360,400,96)),"invalid Unicode monitor rejected before serialization replacement");
        VaultChecks.Require(nameEvents==0 && names.Folders.Count==0 && names.Tags.Count==0 && named.EditVersion==0 && JsonSerializer.Serialize(names.Capture())==namesBefore,"invalid Unicode organization/layout preserves full snapshot/events/basis/draft");names.Clear();
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Title="합성 서식 제목";note.Text="원본\t본문\n";var original=workspace.Capture();workspace.AcceptPrepared(original);
        VaultChecks.ExpectFailure(()=>workspace.ConvertMode(note,"rich"),"format conversion requires explicit loss acknowledgement");
        workspace.ConvertMode(note,"rich",true);var converted=workspace.Capture();
        VaultChecks.Require(converted.SchemaVersion==4 && note.Mode=="rich" && note.Document is not null && converted.History.Single().Mode=="plain" && converted.History.Single().Document is null,"rich conversion has new revision and complete original plain history");
        var bold=JsonNode.Parse(note.Document!.SourceJson)!.AsObject();bold["nodes"]![0]!["runs"]![0]!["bold"]=true;var styled=new StyledDocument(1,bold.ToJsonString());string plain=note.Text;
        workspace.SetRichDocument(note,styled);var formatted=workspace.Capture();VaultChecks.Require(note.Text==plain && formatted.History.Length==2 && formatted.History.Last().Mode=="rich" && formatted.History.Last().Document==converted.Notes.Single().Document,"format-only mutation preserves text and creates immutable full-document revision");
        workspace.AcceptPrepared(formatted);var stable=JsonSerializer.Serialize(formatted);long version=note.EditVersion;workspace.SetRichDocument(note,styled);VaultChecks.Require(note.EditVersion==version && JsonSerializer.Serialize(workspace.Capture())==stable,"identical rich source is no-op");
        VaultChecks.ExpectFailure(()=>note.Text="lossy direct projection edit","rich draft cannot directly overwrite derived Text");
        var typing=new EditingWorkspace(TimeProvider.System);var typed=typing.CreateNote();typing.ConvertMode(typed,"rich",true);
        for(int i=0;i<600;i++)typing.SetRichDocument(typed,RichDocumentCodec.FromPlain("합성 typing "+i));
        VaultChecks.Require(typing.Capture().History.Length==2 && typed.Text=="합성 typing 599","rich typing batches history at preparation rather than exhausting history per keystroke");var dirtyBasis=JsonSerializer.Serialize(typing.FrozenBasis);typing.SetWindowLayout(Guid.NewGuid(),new("memo",typed.Id,"dirty rich layout",0,0,360,400,96));VaultChecks.Require(JsonSerializer.Serialize(typing.FrozenBasis)==dirtyBasis && typing.FrozenBasis.Notes.Single().Text!=typed.Text,"layout change cannot accept dirty rich source/history");typing.Clear();
        RichMutationLimits();
        var copy=workspace.Duplicate(note);VaultChecks.Require(copy.Mode=="rich" && copy.Document==styled && copy.Text==note.Text,"duplicate preserves mode/document exactly");
        var folder=workspace.CreateFolder("合성 서식 폴더");workspace.MoveNotes([note,copy],folder.FolderId);workspace.DeleteNotes([note,copy]);workspace.RestoreNotes([note,copy]);
        VaultChecks.Require(note.Document==styled && copy.Document==styled && workspace.Capture().History.Any(r=>r.Mode=="rich"&&r.Document==styled&&r.Metadata.Deleted),"metadata/batch/rapid trash events preserve full rich content");
        var past=workspace.HistoryFor(note).First(r=>r.Mode=="plain");workspace.RestoreRevision(note,past.RevisionId);VaultChecks.Require(note.Mode=="plain" && note.Document is null && note.Text==original.Notes.Single().Text,"version restore restores complete mode/source not only projected text");
        workspace.ConvertMode(note,"markdown",true);note.Text="**합성**\n![inert](https://example.invalid/image)";workspace.AcceptPrepared(workspace.Capture());workspace.ConvertMode(note,"plain",true);VaultChecks.Require(note.Text.Contains("**합성**") && workspace.HistoryFor(note).Any(r=>r.Mode=="markdown"),"markdown conversion stores exact raw source in immutable history");
        const string opaque="  { \"nodes\" : [ {\"type\":\"future-node\",\"n\":1e+03,\"text\":\"\\uAC00\"} ] }  ";var now=DateTimeOffset.UtcNow;
        var future=new VaultSnapshot(4,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"opaque title","AUTHENTICATED_FUTURE_TEXT","rich"){Document=new(1,opaque)}]);VaultEnvelope.Validate(future);
        var opaqueWorkspace=new EditingWorkspace(TimeProvider.System,future);var opaqueNote=opaqueWorkspace.Notes.Single();opaqueNote.Title="opaque renamed";opaqueNote.Favorite=true;var opaqueSaved=opaqueWorkspace.Capture();
        VaultChecks.Require(opaqueSaved.Notes.Single().Text=="AUTHENTICATED_FUTURE_TEXT" && opaqueSaved.Notes.Single().Document!.SourceJson==opaque && opaqueSaved.History.Single().Document!.SourceJson==opaque,"unknown metadata/title mutation retains authenticated text and byte-exact opaque source");
        VaultChecks.ExpectFailure(()=>opaqueWorkspace.SetRichDocument(opaqueNote,RichDocumentCodec.FromPlain("silent loss")),"unknown original cannot be replaced by projection without explicit conversion");
        VaultChecks.ExpectFailure(()=>MemoApp.Core.Transfer.TextTransfer.Capture(opaqueNote),"unknown text projection cannot silently export an incomplete rich document");
        var root=Path.Combine(Path.GetTempPath(),"memo-rich-store-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret))vault.Save(opaqueSaved);
            using(var reopened=EncryptedVault.Open(root,secret))VaultChecks.Require(reopened.Loaded.Notes.Single().Document!.SourceJson==opaque && reopened.Loaded.History.Single().Document!.SourceJson==opaque,"real encrypted restart preserves source lexemes/escapes/order/whitespace");
            void Reject(Action<JsonObject> alter,string reason)
            {var payload=JsonSerializer.SerializeToNode(opaqueSaved,VaultEnvelope.JsonOptions)!.AsObject();alter(payload);VaultChecks.ExpectFailure(()=>VaultEnvelope.Decrypt(Schema2Checks.Encode(payload,secret),secret),reason);}
            Reject(p=>p["notes"]![0]!.AsObject().Remove("document"),"v4 required note document field");
            Reject(p=>p["history"]![0]!.AsObject().Remove("mode"),"v4 required history mode field");
            Reject(p=>p["history"]![0]!.AsObject().Remove("document"),"v4 required history document field");
            Reject(p=>p["notes"]![0]!["document"]!.AsObject().Remove("sourceJson"),"v4 required exact source field");
            Reject(p=>p["notes"]![0]!["document"]!["extra"]=true,"unknown allowance stays inside SourceJson not typed wrapper");
            Reject(p=>p["notes"]![0]!["mode"]="plain","plain must reject rich data rather than ignore it");
            Reject(p=>p["schemaVersion"]=3,"schema3 cannot carry rich mode/source");
            var knownSnapshot=converted with{Notes=[converted.Notes.Single() with{Text="mismatched projection"}]};VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(knownSnapshot),"known source/projection ordinal mismatch");
            VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(original with{Notes=[original.Notes.Single() with{Title="invalid\uD800"}]}),"Unicode reject before title replacement in JSON serialization");
            var legacyHistory=Guid.NewGuid();var legacyFolder=Guid.NewGuid();var legacyProfile=Guid.NewGuid();var legacyMeta=original.Notes.Single().Metadata with{FolderId=legacyFolder};
            var legacySnapshot=original with{SchemaVersion=3,Notes=[original.Notes.Single() with{Parents=[legacyHistory],Metadata=legacyMeta}],History=[new(note.Id,legacyHistory,[],note.ModifiedAt,"legacy3 title","LEGACY_V3_HISTORY"){Metadata=legacyMeta}],Folders=[new(legacyFolder,null,"legacy3 folder")],UiDevices=[new(legacyProfile,new(true,18,1.25),[new("memo",note.Id,"legacy monitor",0.2,0.3,360,400,96)])]};
            var legacy=JsonSerializer.SerializeToNode(legacySnapshot,VaultEnvelope.JsonOptions)!.AsObject();Schema2Checks.StripDocumentFields(legacy);
            var legacyRoot=Path.Combine(root,"legacy3");Directory.CreateDirectory(legacyRoot);var bytes=Schema2Checks.Encode(legacy,secret);File.WriteAllBytes(Path.Combine(legacyRoot,"current.vault"),bytes);
            using(var session=new SaveCoordinator(EncryptedVault.Open(legacyRoot,secret),TimeProvider.System)){VaultChecks.Require(session.Workspace.Notes.Single().Mode=="plain" && session.Workspace.Notes.Single().Document is null && session.Workspace.Capture().History.Single().Text=="LEGACY_V3_HISTORY" && session.Workspace.Capture().History.Single().Mode=="plain" && session.Workspace.Capture().History.Single().Document is null && session.Workspace.Folders.Single().FolderId==legacyFolder && session.Workspace.GetUiDevice(legacyProfile).Windows.Single().Monitor=="legacy monitor","actual v3 history/organization/UI defaults and references preserved");VaultChecks.Require(session.IsDirty && await session.SaveAsync(),"v3-to4 first encrypted migration");await session.LockAsync();}
            VaultChecks.Require(Directory.GetFiles(legacyRoot,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(bytes)),"first schema4 migration preserves exact previous v3 bytes");
            var failureRoot=Path.Combine(root,"legacy3-fault");Directory.CreateDirectory(failureRoot);File.WriteAllBytes(Path.Combine(failureRoot,"current.vault"),bytes);
            using(var failure=new SaveCoordinator(EncryptedVault.Open(failureRoot,secret,files:new VaultFailureChecks.FaultFiles("pre-flush")),TimeProvider.System)){VaultChecks.Require(!await failure.SaveAsync(),"schema4 migration flush fault reports failure");await failure.LockAsync();}
            VaultChecks.Require(File.ReadAllBytes(Path.Combine(failureRoot,"current.vault")).SequenceEqual(bytes),"failed v3 migration keeps exact original current");
            using(var hidden=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System))
            {
                var draft=hidden.Workspace.Notes.Single();draft.Title=new string('z',257);await hidden.LockAsync();hidden.ResumeHidden(secret);
                draft=hidden.Workspace.Notes.Single();VaultChecks.Require(draft.Mode=="rich" && draft.Document!.SourceJson==opaque && draft.Text=="AUTHENTICATED_FUTURE_TEXT","hidden recovery preserves opaque whole source/mode/authenticated body");draft.Title="fixed opaque";VaultChecks.Require(await hidden.SaveAsync(),"corrected hidden opaque snapshot saves");await hidden.LockAsync();
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
        workspace.Clear();opaqueWorkspace.Clear();VaultChecks.Require(note.Document is null && note.Mode=="plain" && opaqueNote.Document is null,"revocation clears rich document references and mode");
        Console.WriteLine("PASS: schema4 rich/markdown/full-content revisions, clone/batch/restore/opaque protection, actual encrypted source fidelity and v3 exact-byte migration");
    }
}

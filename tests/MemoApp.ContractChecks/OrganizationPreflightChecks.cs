using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using System.Text.Json;
internal static class OrganizationPreflightChecks
{
    private static byte[] Bytes(VaultSnapshot snapshot)=>JsonSerializer.SerializeToUtf8Bytes(snapshot,VaultEnvelope.JsonOptions);
    private static VaultSnapshot NearPayload()
    {
        var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();StyledDocument Document(int padding)=>new(1,"{\"nodes\":[{\"type\":\"future-node\",\"padding\":\""+new string('z',padding)+"\"}]}");
        var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[new(id,Guid.NewGuid(),[],now,now,"near organization","AUTHENTICATED_TEXT","rich"){Document=Document(0)}]){History=Enumerable.Range(0,253).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"old",new string('x',65536))).ToArray()};
        int padding=VaultEnvelope.MaxFile-VaultEnvelope.HeaderSize-148-8-Bytes(snapshot).Length;VaultChecks.Require(padding is >0 and <RichDocumentCodec.MaxSourceBytes,"near envelope organization fixture");snapshot=snapshot with{Notes=[snapshot.Notes.Single() with{Document=Document(padding)}]};VaultEnvelope.Validate(snapshot);return snapshot;
    }
    internal static void Run()
    {
        var errors=new List<string>();
        foreach(string action in new[]{"folder","tag","tag-history"})
        {
            var snapshot=NearPayload();if(action=="tag-history")snapshot=snapshot with{History=Enumerable.Range(0,512).Select(_=>new StoredRevision(snapshot.Notes[0].NoteId,Guid.NewGuid(),[],DateTimeOffset.UtcNow,"old","small")).ToArray()};
            var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var note=workspace.Notes.Single();var before=Bytes(workspace.Capture());var basis=Bytes(workspace.FrozenBasis);var original=note.Document;var metadata=note.Metadata;var modified=note.ModifiedAt;long version=note.EditVersion;int changed=0,properties=0;workspace.Changed+=()=>changed++;note.PropertyChanged+=(_,_)=>properties++;
            try
            {
                VaultChecks.ExpectFailure(()=>{if(action=="folder")workspace.CreateFolder("a");else workspace.SetTags(note,["a"]);},"organization "+action+" budget rejects before mutation");
                VaultChecks.Require(Bytes(workspace.Capture()).SequenceEqual(before)&&Bytes(workspace.FrozenBasis).SequenceEqual(basis)&&note.Document==original&&note.Mode=="rich"&&note.Metadata==metadata&&note.ModifiedAt==modified&&note.EditVersion==version&&workspace.Folders.Count==0&&workspace.Tags.Count==0&&changed==0&&properties==0,"rejected organization preserves full snapshot/basis/source/metadata/version/events");
            }
            catch(Exception e){errors.Add(action+": "+e.Message);}
            finally{workspace.Clear();}
        }
        VaultChecks.Require(errors.Count==0,string.Join(" / ",errors));
        var dirty=new EditingWorkspace(TimeProvider.System);var draft=dirty.CreateNote();dirty.ConvertMode(draft,"rich",true);dirty.SetRichDocument(draft,RichDocumentCodec.FromPlain("DIRTY_RICH_ORIGINAL"));var dirtySource=draft.Document;var dirtyBasis=Bytes(dirty.FrozenBasis);long dirtyVersion=draft.EditVersion;int dirtyProperties=0;draft.PropertyChanged+=(_,_)=>dirtyProperties++;
        dirty.CreateFolder("folder only");VaultChecks.Require(Bytes(dirty.FrozenBasis).SequenceEqual(dirtyBasis)&&draft.Document==dirtySource&&draft.EditVersion==dirtyVersion&&dirtyProperties==0,"successful folder-only change cannot accept dirty rich basis/history or touch draft");
        bool callback=false;dirty.Changed+=()=>{if(callback)return;callback=true;var staged=dirty.Capture();VaultChecks.Require(draft.Metadata.TagIds.Length==1&&dirty.Tags.Single().TagId==draft.Metadata.TagIds.Single()&&dirty.FrozenBasis.Notes.Single().Metadata==draft.Metadata&&dirty.HistoryFor(draft).Any(h=>h.Document==dirtySource&&h.Metadata.TagIds.Length==0),"tag callback sees atomically staged tags/note/basis/history refs");dirty.SetRichDocument(draft,RichDocumentCodec.FromPlain("CALLBACK_RICH_EDIT"));};
        dirty.SetTags(draft,["synthetic tag"]);VaultChecks.Require(callback&&draft.Text=="CALLBACK_RICH_EDIT"&&draft.Metadata.TagIds.Length==1,"tag event cannot overwrite reentrant newer rich edit");dirty.AcceptPrepared(dirty.Capture());var stable=Bytes(dirty.Capture());int events=0;dirty.Changed+=()=>events++;dirty.SetTags(draft,["synthetic tag"]);VaultChecks.Require(events==0&&Bytes(dirty.Capture()).SequenceEqual(stable),"identical tag set has no event/head/history/version noise");dirty.Clear();
        var closing=new EditingWorkspace(TimeProvider.System);var closeNote=closing.CreateNote();closing.AcceptPrepared(closing.Capture());closing.Changed+=closing.Clear;closing.SetTags(closeNote,["close callback tag"]);VaultChecks.Require(closing.Tags.Count==0&&closing.Notes.Count==0&&closing.FrozenBasis.Tags.Length==0&&closeNote.IsClosed,"tag publication clear cannot reinject organization/source after lock");
        Console.WriteLine("PASS: organization whole-payload/history preflight, full rejection invariance, dirty rich basis separation, tag atomic refs/no-op/reentrant edit/clear");
    }
}

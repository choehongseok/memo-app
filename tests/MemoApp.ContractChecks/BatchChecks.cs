using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class BatchChecks
{
    internal static async Task Run()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var a=workspace.CreateNote();a.Title="합성 일괄 A";var b=workspace.CreateNote();b.Text="합성 일괄 B";var other=workspace.CreateNote();other.Title="비선택 합성";var folder=workspace.CreateFolder("합성 일괄 목적지");workspace.AcceptPrepared(workspace.Capture());
        var original=workspace.Capture();workspace.MoveNotes([a,b],folder.FolderId);var moved=workspace.Capture();
        VaultChecks.Require(a.FolderId==folder.FolderId && b.FolderId==folder.FolderId && moved.History.Length==2 && moved.Notes.Single(n=>n.NoteId==other.Id)==original.Notes.Single(n=>n.NoteId==other.Id),"batch move creates selected revisions only and preserves unselected note");
        var movedBytes=Bytes(workspace);int events=0;workspace.Changed+=()=>events++;
        workspace.MoveNotes([a,b],folder.FolderId);VaultChecks.Require(events==0 && Bytes(workspace).SequenceEqual(movedBytes),"no-op batch move has no history/events");
        var foreign=new EditingWorkspace(TimeProvider.System).CreateNote();
        foreach(Action invalid in new Action[]{()=>workspace.DeleteNotes([a,a]),()=>workspace.DeleteNotes([a,foreign]),()=>workspace.MoveNotes([a,b],Guid.NewGuid()),()=>workspace.DeleteNotes([])})
        {VaultChecks.ExpectFailure(invalid,"invalid batch preflight rejects whole selection");VaultChecks.Require(events==0 && Bytes(workspace).SequenceEqual(movedBytes),"batch preflight failure mutates no snapshot/events");}
        int read=0;IEnumerable<NoteDraft> Oversized(){while(true){if(++read>101)throw new Exception("batch enumerated beyond bounded budget");yield return a;}}
        VaultChecks.ExpectFailure(()=>workspace.DeleteNotes(Oversized()),"oversized batch rejected after bounded materialization");VaultChecks.Require(read==101 && events==0 && Bytes(workspace).SequenceEqual(movedBytes),"batch enumerable must stop at101 before mutation");
        workspace.DeleteNotes([a,b]);workspace.RestoreNotes([a,b]);var restored=workspace.Capture();
        VaultChecks.Require(!a.IsDeleted && !b.IsDeleted && restored.Tombstones.Length==0 && restored.History.Count(r=>r.Metadata.Deleted)==2 && restored.History.Length==6,"rapid batch delete/restore retains every deletion event/history without autosave");
        var stable=Bytes(workspace);workspace.DeleteNote(a);var mixed=Bytes(workspace);int beforeMixed=events;
        VaultChecks.ExpectFailure(()=>workspace.DeleteNotes([a,b]),"mixed active/trash delete batch rejects");VaultChecks.ExpectFailure(()=>workspace.RestoreNotes([a,b]),"mixed active/trash restore batch rejects");VaultChecks.Require(Bytes(workspace).SequenceEqual(mixed) && events==beforeMixed,"mixed-state batch rejects without partial changes");workspace.RestoreNote(a);
        var closed=new EditingWorkspace(TimeProvider.System);var closedNote=closed.CreateNote();closed.Clear();VaultChecks.ExpectFailure(()=>workspace.DeleteNotes([b,closedNote]),"closed/foreign draft batch rejected");
        CapacityRejection();ReentrantNotifications();
        var root=Path.Combine(Path.GetTempPath(),"memo-batch-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using(var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System))
            {
                var first=session.Workspace.ImportText("합성 batch disk A","A");var second=session.Workspace.ImportText("합성 batch disk B","B");session.Workspace.DeleteNotes([first,second]);session.Workspace.RestoreNotes([first,second]);
                VaultChecks.Require(await session.SaveAsync(),"batch real encrypted commit");await session.LockAsync();
            }
            using(var vault=EncryptedVault.Open(root,secret))VaultChecks.Require(vault.Loaded.Notes.All(n=>!n.Metadata.Deleted) && vault.Loaded.History.Count(r=>r.Metadata.Deleted)==2,"batch encrypted restart preserves deletion/restoration history");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
        workspace.Clear();VaultChecks.ExpectFailure(()=>workspace.MoveNotes([a,b],folder.FolderId),"closed workspace batch refuses");
        Console.WriteLine("PASS: bounded atomic batch move/trash/restore, whole-state rejection, rapid events, reentrant edit/clear and actual encrypted restart");
    }
    private static byte[] Bytes(EditingWorkspace workspace)=>JsonSerializer.SerializeToUtf8Bytes(workspace.Capture(),VaultEnvelope.JsonOptions);
    private static void CapacityRejection()
    {
        var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var history=Enumerable.Range(0,512).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"h","body")).ToArray();
        var snapshot=new VaultSnapshot(3,Guid.NewGuid(),[new(id,Guid.NewGuid(),[],now,now,"full","body"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"other","other")]){History=history};
        var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var original=Bytes(workspace);int events=0;workspace.Changed+=()=>events++;
        VaultChecks.ExpectFailure(()=>workspace.DeleteNotes(workspace.Notes),"one full history rejects all selected batch notes");VaultChecks.Require(events==0 && Bytes(workspace).SequenceEqual(original) && workspace.Notes.All(n=>n.EditVersion==0&&!n.IsDeleted),"history batch rejection preserves all notes/basis/events");
        var full=Enumerable.Range(0,255).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"h",new string('x',65536))).ToArray();snapshot=snapshot with{History=full};int length=SnapshotSerialization.Bytes(snapshot).Length,limit=VaultEnvelope.MaxFile-VaultEnvelope.HeaderSize-148;
        int last=65536-(length-(limit-8));VaultChecks.Require(last is >=0 and <=65536,"batch payload boundary fixture");full[^1]=full[^1] with{Text=new string('x',last)};VaultEnvelope.Validate(snapshot);
        workspace=new(TimeProvider.System,snapshot);original=Bytes(workspace);events=0;workspace.Changed+=()=>events++;
        VaultChecks.ExpectFailure(()=>workspace.DeleteNotes(workspace.Notes),"whole payload budget rejects entire deletion batch");VaultChecks.Require(events==0 && Bytes(workspace).SequenceEqual(original) && workspace.Notes.All(n=>!n.IsDeleted),"whole payload batch rejection preserves snapshot and event state");workspace.Clear();
    }
    private static void ReentrantNotifications()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var a=workspace.CreateNote();var b=workspace.CreateNote();var folder=workspace.CreateFolder("callback synthetic");workspace.AcceptPrepared(workspace.Capture());bool edited=false;
        a.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.Title)&&!edited){edited=true;b.Text="CALLBACK_BATCH_EDIT_SYNTHETIC";}};
        workspace.MoveNotes([a,b],folder.FolderId);VaultChecks.Require(workspace.Capture().Notes.Single(n=>n.NoteId==b.Id).Text=="CALLBACK_BATCH_EDIT_SYNTHETIC","batch public callback edit must survive accepted state");
        workspace=new(TimeProvider.System);a=workspace.CreateNote();b=workspace.CreateNote();workspace.AcceptPrepared(workspace.Capture());bool cleared=false;
        a.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.Title)&&!cleared){cleared=true;workspace.Clear();}};
        workspace.DeleteNotes([a,b]);VaultChecks.Require(workspace.Notes.Count==0 && workspace.FrozenBasis.Notes.Length==0,"batch callback clear cannot reinject plaintext after revocation");
    }
}

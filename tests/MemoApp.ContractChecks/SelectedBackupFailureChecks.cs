using System.Security.Cryptography;
using System.Collections.Specialized;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class SelectedBackupFailureChecks
{
    internal static async Task Run()
    {
        var now=DateTimeOffset.UnixEpoch;var folder=new StoredFolder(Guid.NewGuid(),null,"folder");var tag=new StoredTag(Guid.NewGuid(),"tag");var stored=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"source","body"){Metadata=new(){FolderId=folder.FolderId,TagIds=[tag.TagId]}};var source=new VaultSnapshot(4,Guid.NewGuid(),[stored]){Folders=[folder],Tags=[tag]};
        var target=new EditingWorkspace(TimeProvider.System,source);
        {
            byte[] before=SnapshotSerialization.Bytes(target.Capture());foreach(var variant in new[]{source with{Folders=[folder with{Name="different"}]},source with{Tags=[tag with{Name="different"}]}}){VaultChecks.ExpectFailure(()=>target.ImportBackupNotes(variant,[stored.NoteId],_=>{}),"Metadata identity conflict refused");Require(SnapshotSerialization.Bytes(target.Capture()).SequenceEqual(before),"Metadata collision preserves exact entire candidate basis");}
            VaultChecks.ExpectFailure(()=>target.ImportBackupNotes(source,[stored.NoteId],_=>throw new InvalidOperationException("Synthetic current-root validation failure")),"Current validator failure precedes publication");Require(SnapshotSerialization.Bytes(target.Capture()).SequenceEqual(before),"Failed current cryptographic preflight leaves exact original");
        }
        target.Clear();var full=new EditingWorkspace(TimeProvider.System);{for(int i=0;i<100;i++)full.CreateNote();full.AcceptPrepared(full.Capture());byte[] before=SnapshotSerialization.Bytes(full.Capture());VaultChecks.ExpectFailure(()=>full.ImportBackupNotes(source,[stored.NoteId],_=>{}),"100-note capacity refuses entire copy");Require(SnapshotSerialization.Bytes(full.Capture()).SequenceEqual(before),"Count failure preserves exact old snapshot");}
        full.Clear();var closed=new EditingWorkspace(TimeProvider.System);{int notifications=0;((INotifyCollectionChanged)closed.Notes).CollectionChanged+=(_,e)=>{if(e.Action!=NotifyCollectionChangedAction.Add)return;notifications++;Require(closed.Notes.Count==2,"Notification sees whole copied batch");closed.Clear();};var second=stored with{NoteId=Guid.NewGuid(),RevisionId=Guid.NewGuid()};var pair=source with{Notes=[stored,second]};var all=closed.ImportBackupNotes(pair,[stored.NoteId,second.NoteId],_=>{});Require(notifications==1&&all.All(n=>n.IsClosed&&n.Text.Length==0)&&closed.Notes.Count==0,"Reentrant close clears copied drafts and stops further publication");}
        var boundedNote=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"near","body");var history=Enumerable.Range(0,255).Select(_=>new StoredRevision(boundedNote.NoteId,Guid.NewGuid(),[],now,"h",new string('x',65536))).ToArray();var large=new VaultSnapshot(5,Guid.NewGuid(),[boundedNote]){AttachmentRootId=Guid.NewGuid(),History=history};int limit=VaultEnvelope.MaxFile-308;int remaining=65536-(SnapshotSerialization.Bytes(large).Length-(limit-8));history[^1]=history[^1] with{Text=new string('x',remaining)};VaultEnvelope.Validate(large);var bounded=new EditingWorkspace(TimeProvider.System,large);byte[] boundedBefore=SnapshotSerialization.Bytes(bounded.Capture());VaultChecks.ExpectFailure(()=>bounded.ImportBackupNotes(source,[stored.NoteId],_=>{}),"Whole 16MiB candidate refusal precedes publication");Require(boundedBefore.SequenceEqual(SnapshotSerialization.Bytes(bounded.Capture())),"Payload refusal preserves original history/root/source");bounded.Clear();
        string root=Path.Combine(Path.GetTempPath(),"memo-selected-save-fault-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using(var seed=EncryptedVault.Create(root,secret,secret))seed.Save(source);byte[] original=File.ReadAllBytes(Path.Combine(root,"current.vault"));using var failed=new SaveCoordinator(EncryptedVault.Open(root,secret,files:new VaultFailureChecks.FaultFiles("pre-flush")),TimeProvider.System);failed.ImportSelectedEncryptedBackup(original,[stored.NoteId],failed.AttachmentPreviewEpoch);Require(!await failed.SaveAsync()&&failed.IsDirty&&failed.Workspace.Notes.Count==2&&original.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Save failure retains whole fresh copy and exact earlier cipher");await failed.LockAsync();
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
        Console.WriteLine("PASS: selected-copy metadata/count/current-auth preflight invariance, notification close and whole dirty save-failure preservation");
    }
    private static void Require(bool value,string message)=>VaultChecks.Require(value,message);
}

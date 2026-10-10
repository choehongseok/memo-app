using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class ExcelImportStorageChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-excel-atomic-storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            var all=new[]{new ImportedText("합성 하나","first",""),new ImportedText("합성 둘","second","")};
            using(var source=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"good"),secret,secret),TimeProvider.System)){var old=source.Workspace.CreateNote();old.Text="original";VaultChecks.Require(await source.SaveAsync(),"Initial accepted original");source.Workspace.ImportTexts(all);VaultChecks.Require(await source.SaveAsync(),"Whole batch durable save");await source.LockAsync();}
            using(var reopened=new SaveCoordinator(EncryptedVault.Open(Path.Combine(root,"good"),secret),TimeProvider.System)){VaultChecks.Require(reopened.Workspace.Notes.Count==3&&reopened.Workspace.Notes.Any(n=>n.Text=="first")&&reopened.Workspace.Notes.Any(n=>n.Text=="second"),"Actual encrypted restart restores every row and original");await reopened.LockAsync();}
            string failing=Path.Combine(root,"failed");using(var seed=EncryptedVault.Create(failing,secret,secret)){seed.Save(new EditingWorkspace(TimeProvider.System).Capture());}byte[] before=File.ReadAllBytes(Path.Combine(failing,"current.vault"));
            using(var failed=new SaveCoordinator(EncryptedVault.Open(failing,secret,files:new VaultFailureChecks.FaultFiles("pre-flush")),TimeProvider.System)){failed.Workspace.ImportTexts(all);VaultChecks.Require(!await failed.SaveAsync()&&failed.IsDirty&&failed.Workspace.Notes.Count==2&&before.SequenceEqual(File.ReadAllBytes(Path.Combine(failing,"current.vault"))),"Post-application flush failure retains entire dirty batch and exact original cipher");await failed.LockAsync();}
            var workspace=new EditingWorkspace(TimeProvider.System);int additions=0;((System.Collections.Specialized.INotifyCollectionChanged)workspace.Notes).CollectionChanged+=(_,e)=>{if(e.Action!=System.Collections.Specialized.NotifyCollectionChangedAction.Add)return;additions++;VaultChecks.Require(workspace.Notes.Count==2,"Lock observer first sees every imported row");workspace.Clear();};var added=workspace.ImportTexts(all);VaultChecks.Require(additions==1&&added.All(n=>n.IsClosed&&n.Text.Length==0)&&workspace.Notes.Count==0,"Reentrant close stops all further notifications and clears every returned draft");
            var full=new EditingWorkspace(TimeProvider.System);for(int i=0;i<100;i++)full.CreateNote();full.AcceptPrepared(full.Capture());byte[] fullBefore=SnapshotSerialization.Bytes(full.Capture());VaultChecks.ExpectFailure(()=>full.ImportTexts(all),"100-note capacity refuses entire candidate");VaultChecks.Require(SnapshotSerialization.Bytes(full.Capture()).SequenceEqual(fullBefore),"Capacity failure leaves exact whole snapshot/basis");full.Clear();
            var now=DateTimeOffset.UnixEpoch;var note=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"near","body");var history=Enumerable.Range(0,255).Select(_=>new StoredRevision(note.NoteId,Guid.NewGuid(),[],now,"h",new string('x',65536))).ToArray();var candidate=new VaultSnapshot(5,Guid.NewGuid(),[note]){AttachmentRootId=Guid.NewGuid(),History=history};int limit=VaultEnvelope.MaxFile-308;int remaining=65536-(SnapshotSerialization.Bytes(candidate).Length-(limit-8));history[^1]=history[^1] with{Text=new string('x',remaining)};VaultEnvelope.Validate(candidate);var bounded=new EditingWorkspace(TimeProvider.System,candidate);byte[] boundedBefore=SnapshotSerialization.Bytes(bounded.Capture());VaultChecks.ExpectFailure(()=>bounded.ImportTexts(all),"Whole payload refusal before registration");VaultChecks.Require(SnapshotSerialization.Bytes(bounded.Capture()).SequenceEqual(boundedBefore),"Payload failure preserves exact old source/history/root");bounded.Clear();
            Console.WriteLine("PASS: all-row encrypted restart, whole dirty preservation on flush failure, complete notification/close and note/payload refusal");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows;
using System.Text.Json;
using System.Collections.Immutable;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task BackupMergeUiRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-backup-merge-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault"),source=Path.Combine(dir,"source.vault");Directory.CreateDirectory(dir);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            var now=DateTimeOffset.UnixEpoch;var ancestor=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"공통 A","ancestor A");
            var history=new StoredRevision(ancestor.NoteId,ancestor.RevisionId,[],now,ancestor.Title,ancestor.Text);
            var current=ancestor with{RevisionId=Guid.NewGuid(),Parents=[ancestor.RevisionId],Title="현재 B",Text="current B"};
            var branch=ancestor with{RevisionId=Guid.NewGuid(),Parents=[ancestor.RevisionId],Title="백업 C",Text="incoming C"};
            var other=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"다른 메모","other");
            var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[current,other]){History=[history]};
            using(var seed=EncryptedVault.Create(root,secret,secret))seed.Save(snapshot);
            main=new MainWindow(root);SetField(main,"searchBlocked",true);main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            WriteBackup(snapshot with{Notes=[branch]});NoteDraft note=active.Workspace.Notes.Single(n=>n.Id==current.NoteId),otherNote=active.Workspace.Notes.Single(n=>n.Id==other.NoteId);
            Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");var list=Control<ListBox>(main,"NotesList");list.SelectedItem=note;
            Require(main.FindName("BackupMergeButton") is Button&&main.FindName("PendingBackupBranchesButton") is Button,"Native same-ID preserve and durable compare buttons are separate from fresh-copy restore");
            var show=typeof(MainWindow).GetMethod("ShowBackupMergeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Task<bool> Show(Func<string?>? choose=null)=>(Task<bool>)show.Invoke(main,[choose??(()=>source)])!;
            var apply=typeof(MainWindow).GetMethod("ConfirmBackupMergeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Task<bool> Apply(BackupMergeWindow viewer,Func<bool> confirm,IAtomicVaultFiles? files=null)=>(Task<bool>)apply.Invoke(main,[viewer,confirm,files])!;
            BackupMergeWindow Viewer()=>Field<HashSet<BackupMergeWindow>>(main,"backupMergeWindows").Single();
            void RoundTrip(){list.SelectedItem=otherNote;list.SelectedItem=note;}
            Require(await active.SaveAsync()&&!active.IsDirty,"Synthetic UI state settled before source/cipher oracle");
            note.Text="dirty checkpoint";byte[] dirtyCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try
            {
                Require(!await Show(()=>null)&&active.IsDirty&&note.Text=="dirty checkpoint"&&dirtyCipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Picker cancellation does not hide a dirty checkpoint");
                Require(await Show()&&!active.IsDirty&&Viewer().CurrentTextView.Text=="dirty checkpoint","Dirty request saves exact current edit before native clean preview");Viewer().Dispose();
            }
            finally{CryptographicOperations.ZeroMemory(dirtyCipher);}
            note.Title=new string('x',257);byte[] failedCheckpointCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try{Require(!await Show()&&active.IsDirty&&note.Title.Length==257&&Field<HashSet<BackupMergeWindow>>(main,"backupMergeWindows").Count==0&&failedCheckpointCipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Failed dirty checkpoint preserves edits/ciphertext and presents no merge confirmation");}
            finally{CryptographicOperations.ZeroMemory(failedCheckpointCipher);note.Title="현재 B";}
            bool interrupted=false;Action<NoteDraft?> checkpointReentry=_=>{interrupted=true;RoundTrip();};active.Workspace.AttachmentReadInvalidating+=checkpointReentry;
            try{Require(!await Show()&&interrupted&&Field<HashSet<BackupMergeWindow>>(main,"backupMergeWindows").Count==0&&note.Text=="dirty checkpoint"&&active.Workspace.Capture().History.All(r=>r.RevisionId!=branch.RevisionId),"Selection away/back during the prepreview checkpoint rejects a displayed token and every incoming revision");}
            finally{active.Workspace.AttachmentReadInvalidating-=checkpointReentry;}
            note.Text="current B";Require(await active.SaveAsync(),"Synthetic current content restored by explicit edit/checkpoint");
            Require(await Show(),"Edit revoke preview opened");var editedViewer=Viewer();byte[] editCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try{note.Title="temporary edit";note.Title="현재 B";Require(editedViewer.IsRevoked&&editedViewer.CurrentTextView.Text.Length==0&&editedViewer.IncomingTextView.Text.Length==0&&!await Apply(editedViewer,()=>throw new Exception("Edited view cannot confirm"))&&editCipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Editing and returning to the original text monotonically revokes the view without writing ciphertext");}
            finally{CryptographicOperations.ZeroMemory(editCipher);}
            Require(await active.SaveAsync(),"Explicit edit checkpoint settles stable head IDs");current=active.Workspace.Capture().Notes.Single(n=>n.NoteId==current.NoteId);
            byte[] before=SnapshotSerialization.Bytes(active.Workspace.Capture()),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try
            {
                Require(!await Show(()=>null)&&Field<HashSet<BackupMergeWindow>>(main,"backupMergeWindows").Count==0,"Merge source picker can cancel without checkpoint/preview");
                Require(!await Show(()=>{RoundTrip();return source;}),"Picker selection away/back monotonically revokes the request");
                Require(await Show(),"Actual native same-ID merge preview is published");var viewer=Viewer();
                Require(viewer.CurrentTitleView.Text=="현재 B"&&viewer.CurrentTextView.Text=="current B"&&viewer.IncomingTitleView.Text=="백업 C"&&viewer.IncomingTextView.Text=="incoming C","Native comparison shows actual immutable current and pending incoming content");
                Require(!await Apply(viewer,()=>false)&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Default-No branch preservation changes neither current source nor ciphertext");
                RoundTrip();Require(viewer.IsRevoked&&viewer.CurrentTitleView.Text.Length==0&&viewer.CurrentTextView.Text.Length==0&&viewer.IncomingTitleView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0&&viewer.BranchesList.Items.Count==0,"Selection away and back immediately clears and revokes every comparison field");
                Require(!await Apply(viewer,()=>throw new Exception("No stale preview confirmation")),"Revoked comparison cannot ask for confirmation");
                list.SelectAll();Require(!await Show()&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Whole selected batch fails if a current live note is absent from backup");list.SelectedItem=note;
                Require(await Show(),"Same-version revoke preview opened");viewer=Viewer();active.Workspace.AcceptPrepared(active.Workspace.Capture());Require(viewer.IsRevoked&&viewer.CurrentTextView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0,"Same-version acceptance immediately clears shown branch content");
                Require(await Show(),"Confirmation reentry preview opened");viewer=Viewer();Require(!await Apply(viewer,()=>{RoundTrip();return true;})&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Selection reentry during native confirmation admits no incoming history");
                Require(await Show(),"Close revoke preview opened");viewer=Viewer();viewer.Close();Require(viewer.IsRevoked&&!await Apply(viewer,()=>throw new Exception("Closed preview must not confirm")),"Closing shown comparison revokes its exact displayed token");
                Require(await Show(),"Native setter revoke preview opened");viewer=Viewer();var nativeText=DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty,typeof(TextBox));EventHandler reenter=(_,_)=>RoundTrip();nativeText.AddValueChanged(viewer.CurrentTitleView,reenter);
                try{viewer.CurrentTitleView.Text="synthetic native setter";Require(viewer.IsRevoked&&viewer.CurrentTitleView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0,"Native text setter selection reentry independently clears every field");}
                finally{nativeText.RemoveValueChanged(viewer.CurrentTitleView,reenter);}
                var direct=new BackupMergeWindow(()=>true,true);var details=ImmutableArray.Create(new BackupMergeBranchComparison(current.NoteId,new(current.RevisionId,current.Title,current.Text,false,current.Mode,current.ModifiedAt,0),new(branch.RevisionId,branch.Title,branch.Text,false,branch.Mode,branch.ModifiedAt,0)));
                EventHandler throws=(_,_)=>throw new InvalidOperationException("Synthetic native comparison setter failure");nativeText.AddValueChanged(direct.IncomingTitleView,throws);
                try{Require(!direct.Publish(details,1,1,true)&&direct.IsRevoked&&direct.CurrentTitleView.Text.Length==0&&direct.CurrentTextView.Text.Length==0&&direct.IncomingTitleView.Text.Length==0&&direct.IncomingTextView.Text.Length==0&&direct.BranchesList.Items.Count==0,"Native publication/clear setter failures still independently purge all comparison fields");}
                finally{nativeText.RemoveValueChanged(direct.IncomingTitleView,throws);direct.Dispose();}
                Require(await Show(),"Changed source preview opened");viewer=Viewer();
                Require(!await Apply(viewer,()=>{File.WriteAllBytes(source,[1,2,3]);return true;})&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Source replacement after displayed confirmation refuses the incoming batch");WriteBackup(snapshot with{Notes=[branch]});
                Require(await Show(),"Paused preservation preview opened");viewer=Viewer();var paused=new PausedBatchFiles();var pending=Apply(viewer,()=>true,paused);
                try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));RoundTrip();paused.Continue.TrySetResult();Require(!await pending&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Selection during encrypted premerge preservation rejects incoming and keeps current ciphertext exact");}
                finally{paused.Continue.TrySetResult();await pending;}
                Require(await Show(),"Successful merge preview opened");viewer=Viewer();var recovery=new RecordingMergeFiles();Require(await Apply(viewer,()=>true,recovery),"Actual native explicit same-ID branch preservation succeeds");
                var merged=active.Workspace.Capture();Require(JsonSerializer.Serialize(merged.Notes.Single(n=>n.NoteId==current.NoteId))==JsonSerializer.Serialize(current)&&merged.History.Any(h=>h.RevisionId==branch.RevisionId&&h.Text==branch.Text),"Current committed head stays exact while incoming branch enters durable history");
                var compare=typeof(MainWindow).GetMethod("ShowPendingBackupBranchesAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Require(await(Task<bool>)compare.Invoke(main,[])!,"Durable pending branches can be opened independently of the backup source");viewer=Viewer();Require(!viewer.PreserveButton.IsEnabled&&viewer.IncomingTextView.Text==branch.Text,"Durable branch comparison has no promotion/resolution action");viewer.Dispose();
                Require(recovery.Destination is{ } preserved&&File.Exists(preserved)&&cipher.SequenceEqual(File.ReadAllBytes(preserved)),"Explicit encrypted premerge preservation is exact committed ciphertext");
            }
            finally{CryptographicOperations.ZeroMemory(before);CryptographicOperations.ZeroMemory(cipher);}
            await active.LockAsync();Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();note=active.Workspace.Notes.Single(n=>n.Id==current.NoteId);otherNote=active.Workspace.Notes.Single(n=>n.Id==other.NoteId);Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");list.SelectedItem=note;
            var pendingView=typeof(MainWindow).GetMethod("ShowPendingBackupBranchesAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Require(await(Task<bool>)pendingView.Invoke(main,[])!&&Viewer().IncomingTextView.Text==branch.Text,"Pending branch identity and content survive native encrypted reopen");var reopenViewer=Viewer();
            var nextBranch=branch with{RevisionId=Guid.NewGuid(),Title="다른 백업 D",Text="incoming D"};WriteBackup(snapshot with{Notes=[nextBranch]});Require(await Show(),"Lock-paused branch preservation preview opened");var lockViewer=Viewer();var lockFiles=new PausedBatchFiles();var lockMerge=Apply(lockViewer,()=>true,lockFiles);
            try
            {
                await lockFiles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!lockMerge.IsCompleted&&lockViewer.IsRevoked&&lockViewer.CurrentTextView.Text.Length==0&&lockViewer.IncomingTextView.Text.Length==0&&!Control<Button>(main,"BackupMergeButton").IsEnabled,"Lock releases keys and independently clears comparison while cipher-only preservation remains paused");
                lockFiles.Continue.TrySetResult();Require(!await lockMerge,"Lock-paused merge applies no incoming branch");await locking;
            }
            finally{lockFiles.Continue.TrySetResult();await lockMerge;}
            Require(reopenViewer.IsRevoked&&reopenViewer.CurrentTextView.Text.Length==0&&reopenViewer.IncomingTextView.Text.Length==0,"Replaced durable comparison also remains fully revoked");
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();Require(active.Workspace.Capture().History.Any(h=>h.RevisionId==branch.RevisionId)&&active.Workspace.Capture().History.All(h=>h.RevisionId!=nextBranch.RevisionId),"Encrypted reopen retains successful C branch and excludes lock-revoked D branch");

            void WriteBackup(VaultSnapshot backup)
            {
                byte[] plain=SnapshotSerialization.Bytes(backup),key=RandomNumberGenerator.GetBytes(32),encrypted=[];
                try{encrypted=VaultEnvelope.Encrypt(plain,key,secret,new(active.VaultIdentity,Guid.NewGuid(),Guid.NewGuid(),1,2,plain.Length));File.WriteAllBytes(source,encrypted);}
                finally{CryptographicOperations.ZeroMemory(plain);CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(encrypted);}
            }
        }
        finally
        {
            if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}
            CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);
        }
    }
    private sealed class RecordingMergeFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal string? Destination;
        public Stream CreateNew(string path){Destination=path;return actual.CreateNew(path);}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);
        public void Move(string temporary,string current)=>throw new NotSupportedException();
        public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }
}

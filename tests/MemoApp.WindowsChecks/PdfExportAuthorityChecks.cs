using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task PdfExportAuthorityRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-pdf-authority-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");
        Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));
            var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var note=active.Workspace.CreateNote();note.Title="합성 권한 PDF";note.Text="원본 ABC";
            var other=active.Workspace.CreateNote();other.Text="다른 합성 메모";
            Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");
            var list=Control<ListBox>(main,"NotesList");list.SelectedItem=note;Require(await active.SaveAsync(),"PDF authority baseline saved");await Idle();list.SelectedItem=note;
            var method=typeof(MainWindow).GetMethod("ExportSelectedPdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Task<bool> Export(string name,Func<bool>? confirm=null,Func<string?>? choose=null,IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(main,[confirm??(()=>true),choose??(()=>Path.Combine(dir,name)),files])!;
            void SelectionRoundTrip(){list.SelectedItem=other;list.SelectedItem=note;}

            var enabled=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.UIElement.IsEnabledProperty,typeof(Button));bool nativeOnce=true;
            EventHandler nativeSelection=(_,_)=>{if(nativeOnce&&!Control<Button>(main,"PdfExportButton").IsEnabled){nativeOnce=false;SelectionRoundTrip();}};
            enabled.AddValueChanged(Control<Button>(main,"PdfExportButton"),nativeSelection);
            try{Require(!await Export("native-roundtrip.pdf",()=>throw new Exception("No consent after native setter selection round trip"))&&!File.Exists(Path.Combine(dir,"native-roundtrip.pdf")),"Native command setter selection round trip revokes before consent");}
            finally{enabled.RemoveValueChanged(Control<Button>(main,"PdfExportButton"),nativeSelection);}
            Require(!await Export("consent-roundtrip.pdf",()=>{SelectionRoundTrip();return true;},()=>throw new Exception("No picker after selection round trip"))&&!File.Exists(Path.Combine(dir,"consent-roundtrip.pdf")),"Consent selection away and back irreversibly revokes PDF export");
            Require(!await Export("picker-roundtrip.pdf",choose:()=>{SelectionRoundTrip();return Path.Combine(dir,"picker-roundtrip.pdf");})&&!File.Exists(Path.Combine(dir,"picker-roundtrip.pdf")),"Picker selection away and back creates no PDF");

            foreach(string mutation in new[]{"edit","selection-roundtrip","same-version-accept"})
            {
                await Idle();list.SelectedItem=note;
                string destination=Path.Combine(dir,mutation+".pdf");var paused=new PausedBatchFiles();
                var pending=Export(mutation+".pdf",files:paused);
                try
                {
                    await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    switch(mutation)
                    {
                        case "edit":note.Text="수정된 합성 ABC";break;
                        case "selection-roundtrip":SelectionRoundTrip();break;
                        case "same-version-accept":
                            long version=note.EditVersion,epoch=active.AttachmentPreviewEpoch;
                            active.Workspace.AcceptPrepared(active.Workspace.Capture());
                            Require(note.EditVersion==version&&active.AttachmentPreviewEpoch!=epoch,"Same-version acceptance advances source authority epoch");break;
                    }
                    byte[] source=PdfAuthorityStableSource(active.Workspace),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
                    try
                    {
                        paused.Continue.TrySetResult();
                        Require(!await pending&&File.Exists(destination)&&new FileInfo(destination).Length==0,"Paused CreateNew "+mutation+" revocation writes no PDF plaintext");
                        Require(source.SequenceEqual(PdfAuthorityStableSource(active.Workspace))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Revoked PDF preserves exact post-mutation source and ciphertext: "+mutation);
                    }
                    finally{CryptographicOperations.ZeroMemory(source);CryptographicOperations.ZeroMemory(cipher);}
                }
                finally{paused.Continue.TrySetResult();await pending;}
                Require(!Field<bool>(main,"pdfExportBusy"),"Revoked PDF releases admission: "+mutation);
            }
            await Idle();list.SelectedItem=note;
            Require(await Export("current.pdf")&&new FileInfo(Path.Combine(dir,"current.pdf")).Length>0,"Fresh current PDF succeeds after revocations and handler cleanup");
            var closePaused=new PausedBatchFiles();var closingExport=Export("closed.pdf",files:closePaused);
            try
            {
                await closePaused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));SetField(main,"confirmedExit",true);main.Close();
                closePaused.Continue.TrySetResult();Require(!await closingExport&&new FileInfo(Path.Combine(dir,"closed.pdf")).Length==0,"Window close during CreateNew writes no PDF plaintext");
            }
            finally{closePaused.Continue.TrySetResult();await closingExport;}
            Require(!active.IsLocked&&Field<bool>(main,"windowClosed"),"Post-close PDF entry fixture keeps the disposed UI CTS and unlocked session");
            bool postCloseConsent=false,postClosePicker=false;var afterClosedFiles=new PausedBatchFiles();
            Require(!await Export("post-closed.pdf",()=>{postCloseConsent=true;return true;},()=>{postClosePicker=true;return Path.Combine(dir,"post-closed.pdf");},afterClosedFiles)
                &&!postCloseConsent&&!postClosePicker&&!afterClosedFiles.Entered.Task.IsCompleted&&!File.Exists(Path.Combine(dir,"post-closed.pdf")),"Already closed PDF command returns false before consent, picker, CreateNew or disposed token access");
        }
        finally
        {
            if(main is not null)
            {
                var active=Field<SaveCoordinator?>(main,"session");
                if(active is not null&&!active.IsLocked)
                {
                    // This fixture deliberately closes before lock; the Closed handler disposed its UI file CTS.
                    if(Field<bool>(main,"windowClosed"))active.Conceal-=(Action)typeof(MainWindow).GetMethod("ConcealViews",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate(typeof(Action),main);
                    await active.LockAsync();
                }
                Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();
            }
            CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);
        }
    }

    private static byte[] PdfAuthorityStableSource(EditingWorkspace workspace)
    {
        var basis=workspace.FrozenBasis;var captured=workspace.Capture();
        // Capture creates fresh provisional revision IDs for dirty drafts; those are not committed identities.
        // Keep every committed ID, history/parent/object/UI field and exact public draft state/version.
        var provisional=captured.Notes.Where(note=>!basis.Notes.Any(old=>old.NoteId==note.NoteId&&old.RevisionId==note.RevisionId)).Select(note=>note.RevisionId).ToHashSet();
        var stable=captured with
        {
            Notes=captured.Notes.Select(note=>provisional.Contains(note.RevisionId)?note with{RevisionId=Guid.Empty}:note).ToArray(),
            Tombstones=captured.Tombstones.Select(note=>provisional.Contains(note.RevisionId)?note with{RevisionId=Guid.Empty}:note).ToArray()
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{CommittedBasis=basis,Current=stable,Drafts=workspace.Notes.ToArray()});
    }
}

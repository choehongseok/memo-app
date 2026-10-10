using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;
using System.Windows.Controls;
using System.ComponentModel;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task ManualBackupRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-manual-modal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Text="synthetic manual latest";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string output=Path.Combine(root,"copy.vault");
            Require(!await main.ChooseManualBackupAsync(()=>null)&&!File.Exists(output),"Cancelled manual picker creates nothing");Require(await main.ChooseManualBackupAsync(()=>output)&&File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault"))),"Manual picker saves exact current encrypted copy");
            var notice=Control<TextBlock>(main,"Notice");var descriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));EventHandler fault=(_,_)=>throw new IOException("Synthetic notice callback");descriptor.AddValueChanged(notice,fault);try{Require(!await main.ChooseManualBackupAsync(()=>throw new IOException("Synthetic picker fault")),"Faulting native failure notice cannot escape manual backup task");}finally{descriptor.RemoveValueChanged(notice,fault);}
            string blocked=Path.Combine(root,"stale.vault");Task<bool>? locking=null;Require(!await main.ChooseManualBackupAsync(()=>{locking=active.LockAsync();return blocked;})&&!File.Exists(blocked),"Lock during picker rejects original modal authority before backup");if(locking is not null)await locking;Require(active.KeysReleased,"Actual picker lock releases keys");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

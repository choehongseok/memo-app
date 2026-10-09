using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task WholeTransferRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-whole-transfer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("WholeTransferButton") is Button,"Explicit whole encrypted migration command is missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Text="합성 이전 자료";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string output=Path.Combine(root,"whole.vault");var method=typeof(MainWindow).GetMethod("ExportWholeVaultAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Require(!await (Task<bool>)method.Invoke(main,[new Func<bool>(()=>false),new Func<string?>(()=>throw new Exception("No picker after cancel")),null])!&&!File.Exists(output),"Cancelled confirmation creates nothing");
            Require(!await (Task<bool>)method.Invoke(main,[new Func<bool>(()=>true),new Func<string?>(()=>null),null])!,"Cancelled destination creates nothing");
            Require(await (Task<bool>)method.Invoke(main,[new Func<bool>(()=>true),new Func<string?>(()=>output),null])!&&File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault"))),"Actual whole export saves latest exact encrypted file");
            var locking=Task.CompletedTask;Require(!await (Task<bool>)method.Invoke(main,[new Func<bool>(()=>{locking=active.LockAsync();return true;}),new Func<string?>(()=>throw new Exception("No picker after lock")),null])!,"Confirmation callback lock invalidates original modal authority");await locking;Require(!Control<Button>(main,"WholeTransferButton").IsEnabled,"Locked whole export disabled");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

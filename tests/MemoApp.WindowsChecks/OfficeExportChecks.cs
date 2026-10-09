using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task OfficeExportRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-office-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("ExcelExportButton") is Button&&main.FindName("WordExportButton") is Button,"Offline Excel and Word native export commands are missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="합성 제목";note.Text="=SUM(1,2) 한글 😀";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");var method=typeof(MainWindow).GetMethod("ExportSelectedOfficeAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            foreach(var format in new[]{OfficeTextFormat.Spreadsheet,OfficeTextFormat.Word})
            {
                string path=Path.Combine(root,format==OfficeTextFormat.Spreadsheet?"selected.xlsx":"selected.docx");
                Require(!await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>false),new Func<string?>(()=>throw new Exception("No Office picker without plaintext confirmation")),null])!&&!File.Exists(path),"Plaintext refusal produces no package");
                Require(await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>true),new Func<string?>(()=>path),null])!,"Actual selected native Office export writes package");using var zip=ZipFile.OpenRead(path);Require(zip.GetEntry("[Content_Types].xml") is not null,"Generated package has content types");
            }
            string fresh=Path.Combine(root,"cancelled.docx");var paused=new PausedBatchFiles();var pending=(Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,new Func<bool>(()=>true),new Func<string?>(()=>fresh),paused])!;await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted,"Lock immediately releases keys while Office worker holds only prepared bytes");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(fresh).Length==0,"Late Office CreateNew cancellation writes no plaintext");await locking;Require(!Control<Button>(main,"ExcelExportButton").IsEnabled&&!Control<Button>(main,"WordExportButton").IsEnabled,"Locked Office commands disabled");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

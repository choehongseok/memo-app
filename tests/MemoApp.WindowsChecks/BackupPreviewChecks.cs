using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task BackupPreviewRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-backup-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("BackupPreviewButton") is Button,"Explicit encrypted backup preview button is missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="합성 preview";note.Text="본문 preview";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string input=Path.Combine(root,"preview.vault");Require(await active.BackupAsync(input),"Native preview actual backup");byte[] original=File.ReadAllBytes(input);var method=typeof(MainWindow).GetMethod("ShowBackupPreviewAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Task<bool> Preview(Func<string?> choose)=>(Task<bool>)method.Invoke(main,[choose])!;
            Require(!await Preview(()=>null),"Cancelled preview creates no viewer");Require(!await Preview(()=>{note.Text="newer";return input;}),"Picker edit revokes old preview authority");Require(await active.SaveAsync(),"Picker edit baseline saved");Require(await Preview(()=>input),"Actual backup preview authenticates earlier encrypted source");
            var viewers=((System.Collections.IEnumerable)Field<object>(main,"backupPreviews")).Cast<Window>().ToArray();Require(viewers.Length==1,"Single detached preview viewer owned by main session");var viewer=viewers[0];var list=(ListBox)viewer.GetType().GetProperty("NotesList")!.GetValue(viewer)!;var body=(TextBox)viewer.GetType().GetProperty("ExcerptView")!.GetValue(viewer)!;list.SelectedIndex=0;Require(body.Text=="본문 preview"&&body.IsReadOnly&&!body.IsUndoEnabled&&File.ReadAllBytes(input).SequenceEqual(original),"Preview shows bounded earlier plaintext without altering original");
            await active.LockAsync();await Idle();Require(!viewer.IsVisible&&list.Items.Count==0&&body.Text.Length==0&&!Control<Button>(main,"BackupPreviewButton").IsEnabled,"Lock revokes/closes viewer and independently clears title/body/list");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task SelectedBackupRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-selected-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="old backup";note.Text="old body";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string input=Path.Combine(root,"restore.vault");Require(await active.BackupAsync(input),"Selected actual source backup");byte[] original=File.ReadAllBytes(input);string hash=Convert.ToHexStringLower(SHA256.HashData(original));note.Text="CURRENT preserved";Require(await active.SaveAsync(),"Selected accepted current");
            var previewMethod=typeof(MainWindow).GetMethod("ShowBackupPreviewAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;Require(await (Task<bool>)previewMethod.Invoke(main,[new Func<string?>(()=>input)])!,"Selected preview created");var viewer=((System.Collections.IEnumerable)Field<object>(main,"backupPreviews")).Cast<BackupPreviewWindow>().Single();Require(viewer.GetType().GetProperty("RestoreButton")?.GetValue(viewer) is Button,"Explicit selected fresh-copy restore button is missing");var restore=typeof(MainWindow).GetMethod("RestoreSelectedBackupAsync",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Selected source/hash/session protected recovery command is missing");
            Task<bool> Copy(Func<int,bool> confirm)=>(Task<bool>)restore.Invoke(main,[viewer,input,hash,new[]{note.Id},confirm])!;
            Require(!await Copy(_=>false)&&active.Workspace.Notes.Count==1,"Cancelled copy confirmation changes nothing");Require(!await Copy(_=>{note.Title="newer";return true;})&&active.Workspace.Notes.Count==1,"Confirm edit revokes selected restore before application");Require(await active.SaveAsync(),"Confirm edit saved");
            Require(await (Task<bool>)previewMethod.Invoke(main,[new Func<string?>(()=>input)])!,"Fresh selected preview after source edit");viewer=((System.Collections.IEnumerable)Field<object>(main,"backupPreviews")).Cast<BackupPreviewWindow>().Single();
            Require(await Copy(count=>count==1)&&active.Workspace.Notes.Count==2&&active.Workspace.Notes.Single(n=>n.Id==note.Id).Text=="CURRENT preserved"&&active.Workspace.Notes.Any(n=>n.Id!=note.Id&&n.Text=="old body")&&File.ReadAllBytes(input).SequenceEqual(original)&&!viewer.IsVisible,"Selected native command adds whole fresh copy, preserves original/current and closes old viewer");await Field<Task>(main,"recentTask");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

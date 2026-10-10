using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task AutomaticBackupRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-auto-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string output=Path.Combine(root,"output");Directory.CreateDirectory(output);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("AutoBackupMode") is ComboBox,"Automatic backup native controls are missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Text="synthetic scheduled backup";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");
            Require((bool)Invoke(main,"ConfigureAutomaticBackups",output,2,true,true,true)!,"Explicit session automatic backup configured");
            var method=typeof(MainWindow).GetMethod("RunAutomaticBackupAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Require(await (Task<bool>)method.Invoke(main,[true,null])!&&Directory.GetFiles(output).Length==1,"Save trigger creates new encrypted automatic copy");Require(!await (Task<bool>)method.Invoke(main,[false,null])!&&Directory.GetFiles(output).Length==1,"Same UTC day never creates repeated daily backups");
            note.Text="last dirty exit";Require(await (Task<bool>)typeof(MainWindow).GetMethod("LockForCloseAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[active])!&&active.KeysReleased&&Directory.GetFiles(output).Length==2,"Exit trigger saves dirty edits then copies final ciphertext after key release");Require(Control<TextBlock>(main,"AutoBackupDirectory").Text.Length==0&&Field<object?>(main,"automaticBackup") is null,"Lock clears session configuration and destination label");
            string latest=Directory.GetFiles(output).OrderBy(File.GetLastWriteTimeUtc).Last();string candidate=EncryptedVault.ImportEncryptedCopy(Path.Combine(root,"restored"),latest,secret);using var restored=EncryptedVault.Open(Path.Combine(root,"restored"),secret,candidate);Require(restored.Loaded.Notes.Single().Text=="last dirty exit","Real exit copy restores exact latest contents");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
        await AutomaticCapacityRaceRun();
    }
    private static async Task AutomaticCapacityRaceRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-capacity-race-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string output=Path.Combine(root,"output");Directory.CreateDirectory(output);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;var paused=new PausedAutomaticCommitFiles();
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret,paused));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();Require((bool)Invoke(main,"ConfigureAutomaticBackups",output,1,false,true,false)!,"Empty directory admitted before save wait");
            var pending=(Task<bool>)typeof(MainWindow).GetMethod("RunAutomaticBackupAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,[false,null])!;await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));string occupied=AutomaticBackupPolicy.NextDestination(output,active.VaultIdentity,1);File.WriteAllBytes(occupied,[1]);paused.Continue.TrySetResult();Require(!await pending&&Directory.GetFiles(output).Length==1,"Capacity filling during source commit refuses CreateNew and retains existing file");
        }
        finally{paused.Continue.TrySetResult();if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null){await active.LockAsync();Invoke(main,"ReleaseSettledSession");}SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private sealed class PausedAutomaticCommitFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path){Entered.TrySetResult();if(!Continue.Task.Wait(TimeSpan.FromSeconds(10)))throw new IOException("Synthetic automatic commit pause");return actual.CreateNew(path);}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string temporary,string current)=>actual.Move(temporary,current);public void Replace(string temporary,string current,string previous)=>actual.Replace(temporary,current,previous);
    }

}

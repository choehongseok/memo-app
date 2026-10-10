using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task AutomaticTrashRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-trash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string output=Path.Combine(root,"output");Directory.CreateDirectory(output);string vaultPath=Path.Combine(root,"vault");byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        var time=DateTimeOffset.UtcNow.AddDays(-31);Guid noteId=Guid.NewGuid(),first=Guid.NewGuid(),deleted=Guid.NewGuid();
        try
        {
            using(var vault=EncryptedVault.Create(vaultPath,secret,secret))using(var prepare=new SaveCoordinator(vault,TimeProvider.System))
            {
                Require(await prepare.PrepareAttachmentsAsync(),"Real anchored root before synthetic dated trash fixture");var before=prepare.Workspace.Capture();
                vault.Save(before with{Notes=[new(noteId,deleted,[first],time,time,"SYNTHETIC_TRASH","# original markdown\n한국어 합성 자료","markdown"){Metadata=new(){Deleted=true}}],History=[new(noteId,first,[],time,"SYNTHETIC_HISTORY","old synthetic plaintext")],Tombstones=[new(noteId,deleted,[first])]});
            }
            main=new MainWindow(vaultPath);main.Show();Require(main.FindName("AutoTrashDays") is ComboBox,"Actual automatic trash controls missing");Invoke(main,"StartSession",EncryptedVault.Open(vaultPath,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();await Idle();
            var held=active.Workspace.Notes.Single();var method=typeof(MainWindow).GetMethod("RunAutomaticTrashAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Task<bool> Run(IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(main,[DateTimeOffset.UtcNow,files])!;
            Require(Field<object?>(main,"automaticTrash") is null&&!await Run()&&Directory.GetFiles(output).Length==0,"Default off never removes synthetic data");
            Require(!(bool)Invoke(main,"ConfigureAutomaticTrash",output,30,(Func<bool>)(()=>false))!&&!(bool)Invoke(main,"ConfigureAutomaticTrash",vaultPath,30,(Func<bool>)(()=>true))!&&!(bool)Invoke(main,"ConfigureAutomaticTrash",output,1,(Func<bool>)(()=>true))!,"Cancel/protected root/invalid period never configure");
            Require((bool)Invoke(main,"ConfigureAutomaticTrash",output,30,(Func<bool>)(()=>true))!,"Explicit session opt-in");
            Require(!await Run(new FailingTrashBackupFiles())&&active.Workspace.Notes.Count==1&&!held.IsClosed,"Failed actual backup forbids removal");
            Require((bool)Invoke(main,"ConfigureAutomaticTrash",output,30,(Func<bool>)(()=>true))!,"Configure before real paused backup");var paused=new PausedTrashBackupFiles();var stale=Run(paused);await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));active.Workspace.SaveSearch(Guid.NewGuid(),"synthetic stale",new(){Query="changed during backup"});paused.Continue.TrySetResult();Require(!await stale&&!held.IsClosed&&active.Workspace.Notes.Count==1&&Directory.GetFiles(output).Length==1,"Metadata mutation during real backup retains copy and refuses stale removal");Require(await active.SaveAsync(),"Settle changed synthetic metadata after stale copy");
            Require((bool)Invoke(main,"ConfigureAutomaticTrash",output,30,(Func<bool>)(()=>true))!,"Configure before lock during backed-up I/O");var lockFiles=new PausedTrashBackupFiles();var lockingRun=Run(lockFiles);await lockFiles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.IsLocked&&active.KeysReleased&&Field<object?>(main,"automaticTrash") is null&&held.Text.Length==0,"Lock clears source/keys/session policy immediately while backup I/O blocked");lockFiles.Continue.TrySetResult();Require(!await lockingRun&&await locking,"Late backed-up result cannot purge locked source");Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(vaultPath,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();held=active.Workspace.Notes.Single();Require(held.IsDeleted&&held.Text=="# original markdown\n한국어 합성 자료"&&Directory.GetFiles(output).Length==2,"Lock race preserves current original deleted content and recoverable copy");
            Require((bool)Invoke(main,"ConfigureAutomaticTrash",output,30,(Func<bool>)(()=>true))!&&await Run(),"Actual encrypted backup then removal then committed8");
            Require(held.IsClosed&&held.Text.Length==0&&active.Workspace.Notes.Count==0&&!active.IsDirty&&active.Workspace.Capture().DiscardedRevisions.Length==2,"Actual WPF purge closes held draft and saves original causal witnesses");
            Require(!await Run()&&Directory.GetFiles(output).Length==3,"One attempt per current UTC day");
            string backup=Directory.GetFiles(output).OrderBy(File.GetLastWriteTimeUtc).Last();string restoredRoot=Path.Combine(root,"restored");string candidate=EncryptedVault.ImportEncryptedCopy(restoredRoot,backup,secret);using(var restored=EncryptedVault.Open(restoredRoot,secret,candidate))Require(restored.Loaded.Notes.Single().Text=="# original markdown\n한국어 합성 자료"&&restored.Loaded.Notes.Single().Mode=="markdown"&&restored.Loaded.History.Single().Text=="old synthetic plaintext","Actual encrypted recovery retains original Markdown and history");
            await active.LockAsync();Require(Field<object?>(main,"automaticTrash") is null&&Control<TextBlock>(main,"AutoTrashDirectory").Text.Length==0&&active.KeysReleased,"Lock disables policy and clears native destination");
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(vaultPath,secret));var reopened=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();Require(Field<object?>(main,"automaticTrash") is null&&reopened.Workspace.Capture().SchemaVersion==8&&reopened.Workspace.Notes.Count==0,"Restart defaultoff and actual saved contentless8");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private sealed class PausedTrashBackupFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path){var stream=actual.CreateNew(path);Entered.TrySetResult();if(!Continue.Task.Wait(TimeSpan.FromSeconds(10))){stream.Dispose();throw new IOException("Synthetic trash pause timeout");}return stream;}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string a,string b)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();
    }
    private sealed class FailingTrashBackupFiles:IAtomicVaultFiles
    {
        public Stream CreateNew(string path)=>throw new IOException("Synthetic backup storage failure");public void FlushToDisk(Stream stream)=>throw new NotSupportedException();public void Move(string a,string b)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();
    }
}

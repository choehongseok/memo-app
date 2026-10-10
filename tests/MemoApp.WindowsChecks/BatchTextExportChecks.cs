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
    private static Task<bool> ExportBatchText(MainWindow main,Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)=>(Task<bool>)(typeof(MainWindow).GetMethod("ExportSelectedTextAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)??throw new Exception("Selected plaintext batch TXT command is missing")).Invoke(main,[confirm,choose,files])!;
    private static async Task BatchTextExportRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-batch-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string output=Path.Combine(root,"output");Directory.CreateDirectory(output);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            var captures=typeof(MainWindow).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t=>t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Any(m=>m.Name.StartsWith("<WriteBatchTextAsync>",StringComparison.Ordinal))).ToArray();var allowed=new[]{typeof(MemoApp.Core.Transfer.PreparedTextBatch),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};Require(captures.Length==1&&captures[0].GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).All(f=>allowed.Contains(f.FieldType)),"Compiled worker closure excludes window/note/session/key graph; file adapter is null in production");
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var session=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var first=session.Workspace.CreateNote();first.Title="합성 제목";first.Text="첫 메모";var second=session.Workspace.CreateNote();second.Title="합성 제목";second.Text="다음 메모";Invoke(main,"RefreshNotes",first);await Idle();var list=Control<ListBox>(main,"NotesList");list.SelectAll();Require(await session.SaveAsync(),"Accepted export baseline");
            Require(main.FindName("BatchTxtExportButton") is Button,"Actual selected batch TXT command is missing");var button=Control<Button>(main,"BatchTxtExportButton");Require(button.IsEnabled,"All active multiple notes enable batch text export");
            Require(!await ExportBatchText(main,()=>false,()=>throw new Exception("No picker before explicit plaintext confirmation"))&&Directory.GetFiles(output).Length==0,"Refused plaintext confirmation creates nothing");
            Require(!await ExportBatchText(main,()=>true,()=>null)&&Directory.GetFiles(output).Length==0,"Folder picker cancellation creates nothing");
            Require(!await ExportBatchText(main,()=>true,()=>Path.Combine(root,"vault"))&&Directory.GetFiles(output).Length==0,"Plaintext batch refuses active encrypted vault directory");
            string inner=Path.Combine(root,"vault","synthetic-subdirectory");Directory.CreateDirectory(inner);Require(!await ExportBatchText(main,()=>true,()=>inner)&&Directory.GetFiles(inner).Length==0&&!Directory.EnumerateFiles(Path.Combine(root,"vault"),"*.txt",SearchOption.AllDirectories).Any(),"Every descendant vault directory refuses plaintext output before capture/write");
            Require(!await ExportBatchText(main,()=>{first.Text="source changed during consent";return true;},()=>throw new Exception("No stale-source picker")),"Modal confirmation source-version change refused");Require(await session.SaveAsync(),"Changed accepted baseline");list.SelectAll();
            Require(!await ExportBatchText(main,()=>true,()=>{list.SelectedItems.Clear();list.SelectedItem=second;return output;})&&Directory.GetFiles(output).Length==0,"Modal folder callback changed selection refuses capture");list.SelectAll();
            Require(await ExportBatchText(main,()=>true,()=>output)&&Directory.GetFiles(output,"*.txt").Length==2,"Real selected command exports both independent files");Require(File.ReadAllText(Path.Combine(output,$"memo-{first.Id:N}.txt")).Contains("source changed")&&File.ReadAllText(Path.Combine(output,$"memo-{second.Id:N}.txt")).Contains("다음 메모"),"Exact selected source text preserved");
            string fresh=Path.Combine(root,"locked-output");Directory.CreateDirectory(fresh);var paused=new PausedBatchFiles();var request=ExportBatchText(main,()=>true,()=>fresh,paused);await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=session.LockAsync();Require(session.IsLocked&&session.KeysReleased&&!button.IsEnabled&&!request.IsCompleted,"WPF lock conceals/releases keys immediately while isolated plaintext worker is blocked");paused.Continue.TrySetResult();Require(!await request,"Cancelled active export cannot report stale UI success");await locking;Require(Directory.GetFiles(fresh).Length==1&&new FileInfo(Directory.GetFiles(fresh).Single()).Length==0,"Post-CreateNew cancellation preserves only empty file and writes no plaintext");
            Require(!await ExportBatchText(main,()=>throw new Exception("Locked consent forbidden"),()=>fresh),"Locked command never requests plaintext consent");
        }
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private sealed class PausedBatchFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path){var stream=actual.CreateNew(path);Entered.TrySetResult();if(!Continue.Task.Wait(TimeSpan.FromSeconds(10))){stream.Dispose();throw new IOException("Synthetic batch pause timed out");}return stream;}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }
}

using MemoApp.Core.Storage;
using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class BatchTextTransferChecks
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-batch-text-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=new EditingWorkspace(TimeProvider.System);
        try
        {
            var first=workspace.CreateNote();first.Title="제목 😀";first.Text="첫 줄\n마지막";var second=workspace.CreateNote();second.Title="제목 😀";second.Text="same title, distinct file";workspace.ConvertMode(second,"rich",true);
            PreparedTextBatch Capture(NoteDraft[] notes)=>BatchTextTransfer.Capture(notes);
            using var batch=Capture([first,second]);var names=batch.FileNames;
            VaultChecks.Require(names.Length==2&&names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==2&&names.All(n=>Path.GetFileName(n)==n&&n.EndsWith(".txt",StringComparison.Ordinal)),"Safe independent file names never use note title");
            BatchTextTransfer.WritePrepared(batch,root);VaultChecks.Require(File.ReadAllText(Path.Combine(root,names[0]),new UTF8Encoding(false,true))=="제목 😀\r\n\r\n첫 줄\r\n마지막"&&File.ReadAllText(Path.Combine(root,names[1])).Contains("same title"),"Every selected plain/rich note exports complete title/body projection");
            byte[] before=File.ReadAllBytes(Path.Combine(root,names[0]));VaultChecks.ExpectFailure(()=>BatchTextTransfer.WritePrepared(batch,root),"Existing destination refuses before overwriting");VaultChecks.Require(File.ReadAllBytes(Path.Combine(root,names[0])).SequenceEqual(before),"Existing exported bytes unchanged");
            string collision=Path.Combine(root,"collision");Directory.CreateDirectory(collision);File.WriteAllText(Path.Combine(collision,names[1]),"preserve existing");VaultChecks.ExpectFailure(()=>BatchTextTransfer.WritePrepared(batch,collision),"Last-file collision refuses whole preflight");VaultChecks.Require(!File.Exists(Path.Combine(collision,names[0]))&&File.ReadAllText(Path.Combine(collision,names[1]))=="preserve existing","Whole preflight creates no earlier files when final target exists");
            VaultChecks.ExpectFailure(()=>Capture([]),"Empty selection rejected");VaultChecks.ExpectFailure(()=>Capture([first,first]),"Duplicate selection rejected");
            workspace.DeleteNote(second);VaultChecks.ExpectFailure(()=>Capture([first,second]),"Entire capture rejects a trashed selected note before creating files");
            string cancelled=Path.Combine(root,"cancelled");Directory.CreateDirectory(cancelled);using var stop=new CancellationTokenSource();stop.Cancel();ExpectCancelled(()=>BatchTextTransfer.WritePrepared(batch,cancelled,stop.Token));VaultChecks.Require(Directory.GetFiles(cancelled).Length==0,"Cancelled batch creates no plaintext files");
            string between=Path.Combine(root,"between");Directory.CreateDirectory(between);using var midway=new CancellationTokenSource();ExpectCancelled(()=>BatchTextTransfer.WritePrepared(batch,between,midway.Token,new ExportFiles(()=>midway.Cancel(),false)));VaultChecks.Require(File.Exists(Path.Combine(between,names[0]))&&!File.Exists(Path.Combine(between,names[1])),"Cancellation between files preserves completed first and creates no second");
            string failed=Path.Combine(root,"failed");Directory.CreateDirectory(failed);VaultChecks.ExpectFailure(()=>BatchTextTransfer.WritePrepared(batch,failed,default,new ExportFiles(()=>{},true)),"Second flush fault surfaced");VaultChecks.Require(File.ReadAllBytes(Path.Combine(failed,names[0])).SequenceEqual(before)&&File.Exists(Path.Combine(failed,names[1])),"Flush failure retains exact first and unverified second; no automatic deletion");
            var owned=batch.Entries.Select(e=>e.Payload.Bytes).ToArray();VaultChecks.Require(owned.All(bytes=>bytes.Any(b=>b!=0)),"Fixture retains real owned UTF8 byte arrays");
            batch.Dispose();VaultChecks.ExpectFailure(()=>BatchTextTransfer.WritePrepared(batch,cancelled),"Disposed batch cannot write");
            VaultChecks.Require(owned.All(bytes=>bytes.All(b=>b==0)),"Batch finally disposal zeroes actual captured arrays");
            workspace.RestoreNote(second);using(var pausedBatch=Capture([first,second]))
            {
                string paused=Path.Combine(root,"paused");Directory.CreateDirectory(paused);var waiting=new PauseExportFiles();using var cancel=new CancellationTokenSource();var buffers=pausedBatch.Entries.Select(e=>e.Payload.Bytes).ToArray();var worker=Task.Run(()=>BatchTextTransfer.WritePrepared(pausedBatch,paused,cancel.Token,waiting));VaultChecks.Require(waiting.Entered.Wait(TimeSpan.FromSeconds(10)),"Paused CreateNew entered");cancel.Cancel();VaultChecks.Require(!worker.IsCompleted&&buffers.All(bytes=>bytes.Any(b=>b!=0)),"Cancellation does not prematurely zero buffers still owned by paused worker");waiting.Continue.Set();ExpectCancelled(()=>worker.GetAwaiter().GetResult());VaultChecks.Require(new FileInfo(Path.Combine(paused,pausedBatch.FileNames[0])).Length==0&&Directory.GetFiles(paused).Length==1,"Cancellation after blocked CreateNew writes no plaintext and preserves empty created file");pausedBatch.Dispose();VaultChecks.Require(buffers.All(bytes=>bytes.All(b=>b==0)),"Worker settles before all captured buffers zero");
            }
            VaultChecks.Require(first.Text=="첫 줄\n마지막"&&first.Title=="제목 😀","Batch transfer never mutates source notes");
            var limitWorkspace=new EditingWorkspace(TimeProvider.System);try
            {
                var hundred=Enumerable.Range(0,100).Select(_=>limitWorkspace.CreateNote()).ToArray();using(var exactCount=BatchTextTransfer.Capture(hundred))VaultChecks.Require(exactCount.FileNames.Length==100,"Exactly100 distinct notes accepted");
                bool overEnumerated=false;IEnumerable<NoteDraft> OversizedSelection(){foreach(var item in hundred)yield return item;yield return hundred[0];overEnumerated=true;throw new Exception("Enumeration must stop at101");}
                VaultChecks.ExpectFailure(()=>BatchTextTransfer.Capture(OversizedSelection()),"101 notes rejected");VaultChecks.Require(!overEnumerated,"Untrusted enumerable bounded at101 before capture");
                // In-memory unaccepted drafts deliberately reach the transfer limit independently of the encrypted snapshot budget.
                for(int i=0;i<85;i++)hundred[i].Text=new string('가',65536);hundred[85].Text=new string('x',65536);
                using(var exactBytes=BatchTextTransfer.Capture(hundred.Take(86)))VaultChecks.Require(exactBytes.Entries.Sum(e=>e.Payload.Bytes.Length)==16*1024*1024,"Exactly16MiB captured payload accepted");hundred[86].Text="x";VaultChecks.ExpectFailure(()=>BatchTextTransfer.Capture(hundred.Take(87)),"16MiB plus one byte rejected before any destination");
            }
            finally{limitWorkspace.Clear();}
        }
        finally{workspace.Clear();Directory.Delete(root,true);}
        Console.WriteLine("PASS: bounded whole-selection TXT capture, independent safe names, complete Unicode/rich text, original preservation, cancellation/disposal and no overwrite");
    }
    private static void ExpectCancelled(Action action){try{action();}catch(OperationCanceledException){return;}throw new Exception("Expected bounded batch cancellation");}
    private sealed class ExportFiles(Action afterFlush,bool failSecond):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();private int count;
        public Stream CreateNew(string path)=>actual.CreateNew(path);
        public void FlushToDisk(Stream stream){if(++count==2&&failSecond)throw new IOException("Synthetic second flush fault");actual.FlushToDisk(stream);afterFlush();}
        public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }
    private sealed class PauseExportFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal readonly ManualResetEventSlim Entered=new(),Continue=new();
        public Stream CreateNew(string path){var output=actual.CreateNew(path);Entered.Set();if(!Continue.Wait(TimeSpan.FromSeconds(10))){output.Dispose();throw new IOException("Synthetic CreateNew pause timed out");}return output;}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }
}

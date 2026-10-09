using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class AttachmentFileTransferChecks
{
    private static bool Write(AttachmentReadLease lease,string path,string root,CancellationToken token=default,IAtomicVaultFiles? files=null)
    {
        var type=typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Transfer.AttachmentFileTransfer");VaultChecks.Require(type is not null,"Explicit authenticated attachment file transfer is missing");
        try{return (bool)type!.GetMethod("Write")!.Invoke(null,[lease,path,root,token,files])!;}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static byte[] Owned(AttachmentReadLease lease)=>(byte[])typeof(AttachmentReadLease).GetField("bytes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(lease)!;
    internal static async Task Run()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-attachment-file-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");Directory.CreateDirectory(dir);var secret=EncryptedVault.GenerateRecoverySecret();var body=RandomNumberGenerator.GetBytes(130001);
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"File transfer actual root");var id=owner.AttachBytes(note,body,"synthetic.bin","application/octet-stream",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"File transfer saved actual object");
            var rootMethod=typeof(SaveCoordinator).GetMethod("AttachmentExportRoot");VaultChecks.Require(rootMethod is not null,"Attachment destination root must come from current coordinator authority");VaultChecks.Require((string)rootMethod!.Invoke(owner,[note,id,note.EditVersion,owner.AttachmentPreviewEpoch])! == Path.GetFullPath(root),"Actual protected data root scalar");
            AttachmentReadLease Borrow()=>owner.CreateAttachmentReadLease(note,id,note.EditVersion);
            using(var lease=Borrow()){var bytes=Owned(lease);VaultChecks.Require(Write(lease,Path.Combine(dir,"copy.bin"),root)&&File.ReadAllBytes(Path.Combine(dir,"copy.bin")).SequenceEqual(body)&&bytes.All(b=>b==0),"Explicit file copy exact authenticated bytes and single-use zero");}
            using(var lease=Borrow()){var bytes=Owned(lease);VaultChecks.ExpectFailure(()=>Write(lease,Path.Combine(dir,"copy.bin"),root),"No overwrite existing plaintext file");VaultChecks.Require(bytes.All(b=>b==0)&&File.ReadAllBytes(Path.Combine(dir,"copy.bin")).SequenceEqual(body),"Existing destination preserved and failed grant zeroed");}
            foreach(string bad in new[]{Path.Combine(root,"plaintext.bin"),Path.Combine(root,"child","plaintext.bin"),"file://server/unsafe.bin","safe.bin:stream","\\\\server\\share\\file.bin"})using(var lease=Borrow()){var bytes=Owned(lease);VaultChecks.ExpectFailure(()=>Write(lease,bad,root),"Vault/network/URI/ADS destination refused");VaultChecks.Require(bytes.All(b=>b==0),"Refused target disposes unused plaintext grant");}
            string link=Path.Combine(dir,"linked");Directory.CreateSymbolicLink(link,root);using(var lease=Borrow())VaultChecks.ExpectFailure(()=>Write(lease,Path.Combine(link,"unsafe.bin"),root),"Linked destination parent refused");Directory.Delete(link);
            using(var cancel=new CancellationTokenSource())using(var lease=Borrow()){var bytes=Owned(lease);cancel.Cancel();bool denied=false;try{Write(lease,Path.Combine(dir,"pre-cancel.bin"),root,cancel.Token);}catch(OperationCanceledException){denied=true;}VaultChecks.Require(denied&&!File.Exists(Path.Combine(dir,"pre-cancel.bin"))&&bytes.All(b=>b==0),"Already canceled copy creates no plaintext and disposes grant");}
            using(var cancel=new CancellationTokenSource())using(var lease=Borrow()){var bytes=Owned(lease);bool denied=false;try{Write(lease,Path.Combine(dir,"create-cancel.bin"),root,cancel.Token,new CancelFiles(cancel));}catch(OperationCanceledException){denied=true;}VaultChecks.Require(denied&&new FileInfo(Path.Combine(dir,"create-cancel.bin")).Length==0&&bytes.All(b=>b==0),"Cancel during CreateNew prevents first plaintext write, retained owned empty file");}
            using(var lease=Borrow()){var bytes=Owned(lease);VaultChecks.ExpectFailure(()=>Write(lease,Path.Combine(dir,"flush-fail.bin"),root,files:new VaultFailureChecks.FaultFiles("pre-flush")),"Flush failure remains failure");VaultChecks.Require(bytes.All(b=>b==0)&&File.ReadAllBytes(Path.Combine(dir,"flush-fail.bin")).SequenceEqual(body),"Failed owned plaintext copy retained, never deleted");}
            using var started=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using(var lease=Borrow())
            {
                var bytes=Owned(lease);var files=new PausedFiles(started,release);var write=Task.Run(()=>Write(lease,Path.Combine(dir,"locked-copy.bin"),root,files:files));
                try{VaultChecks.Require(started.Wait(5000),"Attachment write paused inside actual CreateNew");VaultChecks.Require(await owner.LockAsync()&&owner.KeysReleased&&!owner.WhenAttachmentReadsIdle.IsCompleted&&bytes.SequenceEqual(body),"Lock releases keys before running consumer cleanup");}finally{release.Set();}
                VaultChecks.Require(!await write&&bytes.All(b=>b==0)&&owner.WhenAttachmentReadsIdle.IsCompleted,"Revoked running copy cannot authorize external launch and zeros after completion");
            }
            Console.WriteLine("PASS: explicit bounded authenticated attachment copy, exact bytes/zero, vault/network/link refusal, no overwrite, create cancellation, partial preservation and running lock immediate key release");
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(body);if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    private sealed class CancelFiles(CancellationTokenSource cancel):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();public Stream CreateNew(string path){var result=actual.CreateNew(path);cancel.Cancel();return result;}public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string a,string b)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();
    }
    private sealed class PausedFiles(ManualResetEventSlim started,ManualResetEventSlim release):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();public Stream CreateNew(string path){var result=actual.CreateNew(path);started.Set();if(!release.Wait(10000)){result.Dispose();throw new IOException("Synthetic pause timeout");}return result;}public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string a,string b)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();
    }
}

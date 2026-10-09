using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class PreparedBackupChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-keyless-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            var capture=typeof(EncryptedVault).GetMethod("CaptureCommittedCopy")??throw new Exception("Authenticated keyless encrypted backup capture is missing");
            using var vault=EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret);using var session=new SaveCoordinator(vault,TimeProvider.System);var note=session.Workspace.CreateNote();note.Text="합성 독립 백업";VaultChecks.Require(await session.PrepareAttachmentsAsync(),"root prepared");session.AttachBytes(note,new byte[]{3,4,5},"synthetic.bin","application/octet-stream",note.EditVersion);VaultChecks.Require(await session.SaveAsync(),"accepted original and attachment");
            var prepared=(PreparedEncryptedCopy)capture.Invoke(vault,[])!;byte[] owned=(byte[])typeof(PreparedEncryptedCopy).GetField("bytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(prepared)!;var write=prepared.GetType().GetMethod("WriteTo")!;byte[] original=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));await session.LockAsync();try{vault.CaptureCommittedCopy();throw new Exception("Locked capture accepted");}catch(InvalidOperationException){}VaultChecks.Require(session.KeysReleased,"keys released before backup worker");string output=Path.Combine(root,"fresh.vault");write.Invoke(prepared,[output,null]);VaultChecks.Require(original.SequenceEqual(File.ReadAllBytes(output)),"Exact current ciphertext copied after key release");            try{prepared.WriteTo(output);throw new Exception("Existing backup accepted");}catch(IOException){}VaultChecks.Require(original.SequenceEqual(File.ReadAllBytes(output)),"Existing encrypted backup never overwritten");
            try{prepared.WriteTo(Path.Combine(root,"vault","inside.vault"));throw new Exception("Active vault output accepted");}catch(IOException){}
            string corrupt=Path.Combine(root,"corrupt.vault");try{prepared.WriteTo(corrupt,new CorruptFiles());throw new Exception("Tampered backup verification accepted");}catch(IOException){}VaultChecks.Require(File.Exists(corrupt),"Failed verification retains unverified encrypted file");
            prepared.Dispose();VaultChecks.Require(owned.All(b=>b==0),"Owned detached bytes zeroed only after settled worker");
            try{write.Invoke(prepared,[Path.Combine(root,"disposed.vault"),null]);throw new Exception("Disposed copy accepted");}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException){}
            EncryptedVault.ImportEncryptedCopy(Path.Combine(root,"new-pc"),output,secret);var candidate=EncryptedVault.InspectCandidates(Path.Combine(root,"new-pc"),secret).Single();using var restored=EncryptedVault.Open(Path.Combine(root,"new-pc"),secret,candidate.Name);VaultChecks.Require(restored.Loaded.Notes.Single().Text=="합성 독립 백업"&&restored.Loaded.AttachmentObjects.Length==1,"One backup retains metadata and encrypted attachment on fresh root");
            using var exiting=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"exit-source"),secret,secret),TimeProvider.System);var dirty=exiting.Workspace.CreateNote();dirty.Text="final dirty exit";var pause=new PauseFiles();string exit=Path.Combine(root,"exit-final.vault");var closing=exiting.LockWithBackupAsync(exit,pause);VaultChecks.Require(exiting.IsLocked&&exiting.KeysReleased&&dirty.Text.Length==0,"Exit immediately conceals/releases keys before file wait");await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));VaultChecks.Require(!closing.IsCompleted&&exiting.IsBusy,"Exit waits detached ciphertext copy before disposal");pause.Continue.TrySetResult();VaultChecks.Require(await closing.WaitAsync(TimeSpan.FromSeconds(10)),"No lock/backup self wait; final commit succeeds");string exitCandidate=EncryptedVault.ImportEncryptedCopy(Path.Combine(root,"exit-restored"),exit,secret);using var final=EncryptedVault.Open(Path.Combine(root,"exit-restored"),secret,exitCandidate);VaultChecks.Require(final.Loaded.Notes.Single().Text=="final dirty exit","Exit backup contains final unsaved edits");
            Console.WriteLine("PASS: detached authenticated ciphertext after key release, exact originals, fresh root, final dirty exit and no self-wait");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private sealed class CorruptFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real=new();public Stream CreateNew(string path)=>real.CreateNew(path);public void FlushToDisk(Stream stream){stream.Position=0;stream.WriteByte(0);real.FlushToDisk(stream);}public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }
    private sealed class PauseFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real=new();internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path){Entered.TrySetResult();if(!Continue.Task.Wait(TimeSpan.FromSeconds(10)))throw new IOException("Synthetic pause");return real.CreateNew(path);}
        public void FlushToDisk(Stream stream)=>real.FlushToDisk(stream);public void Move(string temporary,string current)=>throw new NotSupportedException();public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }

}

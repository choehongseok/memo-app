using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

internal static class BackupMergeLifecycleChecks
{
    internal static async Task Run()
    {
        await TokenAuthority();await FailureBoundaries();await AttachmentsAndDocuments();
        await CandidateRevocation();WrongOwner();
    }
    private static async Task TokenAuthority()
    {
        using var f=new Fixture();using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);
        var preview=await f.Preview(view);
        VaultChecks.ExpectFailure(()=>f.Session.ConfirmBackupMerge(preview.Token),"An undisplayed preview cannot be confirmed");
        view.MarkDisplayed(preview.Token);var confirmed=f.Session.ConfirmBackupMerge(preview.Token);
        f.Session.Workspace.Notes.Single().Title="changed after preview";
        Require(preview.Token.Prepared is null,"Idle edit releases pending preview candidate references");
        VaultChecks.ExpectFailure(()=>f.Session.MergeEncryptedBackupAsync(confirmed,f.Cipher),"Dirty/change revokes confirmed token, no autosave stand-in");
        Require(f.Session.IsDirty&&!f.Session.Workspace.Capture().History.Any(r=>r.RevisionId==f.Branch.RevisionId),"Revoked preview leaves dirty edit and no incoming branch");
        Require(await f.Session.SaveAsync(),"Save existing edit for next genuine preview");
        var another=await f.Preview(view);view.MarkDisplayed(another.Token);var otherConfirmed=f.Session.ConfirmBackupMerge(another.Token);
        using(var other=new Fixture())VaultChecks.ExpectFailure(()=>other.Session.MergeEncryptedBackupAsync(otherConfirmed,f.Cipher),"Token cannot cross session owners");
        view.Dispose();VaultChecks.ExpectFailure(()=>f.Session.MergeEncryptedBackupAsync(otherConfirmed,f.Cipher),"Closed view revokes token");
        bool mutate=false;byte[] borrowed=(byte[])f.Cipher.Clone();Guid[] selection=[f.Branch.NoteId];
        using var ownedView=f.Session.CreateBackupMergeView(()=>{if(mutate){Array.Clear(borrowed);selection[0]=Guid.NewGuid();mutate=false;}return true;},()=>true);mutate=true;
        var owned=await f.Session.PreviewEncryptedBackupMergeAsync(borrowed,selection,f.SourcePath,f.Session.AttachmentPreviewEpoch,ownedView);
        Require(owned.SelectedIds.SequenceEqual(new[]{f.Branch.NoteId}),"Selection and cipher are cloned before first host callback");
        ownedView.MarkDisplayed(owned.Token);var ownedConfirmed=f.Session.ConfirmBackupMerge(owned.Token);
        var applied=await f.Session.MergeEncryptedBackupAsync(ownedConfirmed,f.Cipher);Require(applied.Disposition==BackupMergeDisposition.AppliedAndSaved,"Caller buffer mutation cannot change authenticated owned import");
        Console.WriteLine("PASS: O06 undisplayed/cross-session/dirty/closed token refusal and borrowed-input ownership");
    }
    private static async Task CandidateRevocation()
    {
        using(var f=new Fixture())
        {
            using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var p=await f.Preview(view);
            f.Session.Workspace.AcceptPrepared(f.Session.Workspace.Capture());Require(p.Token.Prepared is null,"AcceptPrepared releases idle candidate even without public Changed");
            var next=await f.Preview(view);await f.Session.LockAsync();Require(next.Token.Prepared is null,"Idle preview lock revokes and releases candidate");
        }
        using(var f=new Fixture())
        {
            BackupMergePreviewToken? failed=null;var field=typeof(SaveCoordinator).GetField("mergePreview",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            using var view=f.Session.CreateBackupMergeView(()=>
            {if(field.GetValue(f.Session) is BackupMergePreviewToken token){failed=token;throw new InvalidOperationException("synthetic final preview callback");}return true;},()=>true);
            bool refused=false;try{await f.Preview(view);}catch(InvalidOperationException){refused=true;}
            Require(refused&&failed is {Prepared:null}&&field.GetValue(f.Session) is null,"Failure after token registration releases candidate/registry and issues no preview");
        }
        Console.WriteLine("PASS: O06 idle edit/AcceptPrepared/lock and failed-final-callback candidate revocation");
    }
    private static void WrongOwner()
    {
        using var f=new Fixture();int owner=Environment.CurrentManagedThreadId;
        using var view=f.Session.CreateBackupMergeView(()=>true,()=>Environment.CurrentManagedThreadId==owner);
        var p=f.Preview(view).GetAwaiter().GetResult();bool refused=false;
        int invokedThread=0;var foreign=new Thread(()=>{invokedThread=Environment.CurrentManagedThreadId;try{view.MarkDisplayed(p.Token);}catch(InvalidOperationException){refused=true;}});foreign.Start();Require(foreign.Join(TimeSpan.FromSeconds(10)),"Foreign-owner fixture joined");
        Require(invokedThread!=owner&&refused&&view.Displayed is null,"Wrong owner cannot publish or confirm a token");
        Console.WriteLine("PASS: O06 explicit owner-access policy rejects foreign thread entry");
    }
    private static async Task FailureBoundaries()
    {
        using(var f=new Fixture(new VaultFailureChecks.FaultFiles("pre-flush")))
        {
            f.Session.Workspace.Notes.Single().Title="checkpoint dirty";using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);
            bool refused=false;try{await f.Preview(view);}catch(InvalidOperationException){refused=true;}
            Require(refused&&f.Session.IsDirty&&f.Session.Workspace.Notes.Single().Title=="checkpoint dirty"&&!f.Session.Workspace.Capture().History.Any(h=>h.RevisionId==f.Branch.RevisionId),"Checkpoint failure retains dirty and never issues merge preview or imports branch");
        }
        using(var f=new Fixture())
        {
            using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var confirmed=await f.Confirm(view);byte[] before=SnapshotSerialization.Bytes(f.Session.Workspace.Capture());
            var failed=await f.Session.StartBackupMerge(confirmed,f.Cipher,new FailingRecovery());
            Require(failed.Disposition==BackupMergeDisposition.NotApplied&&before.SequenceEqual(SnapshotSerialization.Bytes(f.Session.Workspace.Capture()))&&!f.Session.IsDirty,"Premerge recovery create failure leaves exact current state");
        }
        using(var f=new Fixture(new VaultFailureChecks.FaultFiles("pre-flush")))
        {
            using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var result=await f.Session.MergeEncryptedBackupAsync(await f.Confirm(view),f.Cipher);
            Require(result.Disposition==BackupMergeDisposition.AppliedDirty&&f.Session.IsDirty&&f.Session.Workspace.Capture().History.Any(h=>h.RevisionId==f.Branch.RevisionId)&&f.Session.Workspace.Notes.Single().Text=="B","Merged save fault retains complete original head plus incoming dirty branch");
        }
        using(var f=new Fixture())
        {
            using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var confirmed=await f.Confirm(view);using var blocked=new BlockedRecovery();var merging=f.Session.StartBackupMerge(confirmed,f.Cipher,blocked);
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));File.WriteAllBytes(f.SourcePath,[1,2,3]);blocked.Release.Set();var result=await merging;
            Require(result.Disposition==BackupMergeDisposition.NotApplied&&!f.Session.Workspace.Capture().History.Any(h=>h.RevisionId==f.Branch.RevisionId),"Source replacement during premerge preservation rejects before apply");
        }
        using(var f=new Fixture())
        {
            using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var confirmed=await f.Confirm(view);Action thrower=()=>throw new InvalidOperationException("synthetic postapply notification");f.Session.Workspace.Changed+=thrower;
            var result=await f.Session.MergeEncryptedBackupAsync(confirmed,f.Cipher);f.Session.Workspace.Changed-=thrower;
            Require(result.Disposition==BackupMergeDisposition.AppliedDirty&&f.Session.IsDirty&&f.Session.Workspace.Capture().History.Any(h=>h.RevisionId==f.Branch.RevisionId),"Post-whole-swap observer throw is AppliedDirty, not rollback");
        }
        Console.WriteLine("PASS: O06 checkpoint/preservation/merged-save/source-replacement/postapply observer failure boundaries");
    }
    private static async Task AttachmentsAndDocuments()
    {
        using var f=new Fixture();var note=f.Session.Workspace.Notes.Single();Require(await f.Session.PrepareAttachmentsAsync(),"Synthetic anchored current root");
        var current=f.Session.Workspace.Capture();byte[] rootKey=(byte[])typeof(EncryptedVault).GetField("attachmentRootKey",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Vault)!;
        byte[] objectBytes=[0,1,255,7];var obj=AttachmentObjectCodec.Encrypt(objectBytes,f.Session.VaultIdentity,current.AttachmentRootId,rootKey,"history-only.bin","application/octet-stream");
        var historical=new StoredRevision(note.Id,Guid.NewGuid(),[f.Ancestor.RevisionId],DateTimeOffset.UnixEpoch,"historical attached","C"){AttachmentIds=[obj.ObjectId]};
        var rich=new MemoApp.Core.Documents.StyledDocument(1," { \"nodes\": [{\"type\":\"future-node\",\"opaque\":1e+03}] } ");
        var head=f.Branch with{Parents=[historical.RevisionId],Mode="rich",Document=rich,Text="opaque authenticated text"};
        var backup=new VaultSnapshot(5,current.DeviceId,[head]){History=[f.AncestorRevision,historical],AttachmentRootId=current.AttachmentRootId,AttachmentObjects=[obj]};
        f.ReplaceSource(backup,rootKey);using var view=f.Session.CreateBackupMergeView(()=>true,()=>true);var result=await f.Session.MergeEncryptedBackupAsync(await f.Confirm(view),f.Cipher);
        var after=f.Session.Workspace.Capture();Require(result.Disposition==BackupMergeDisposition.AppliedAndSaved&&after.AttachmentObjects.Any(o=>AttachmentValidation.SameObject(o,obj))&&after.History.Any(h=>h.RevisionId==head.RevisionId&&h.Document!.SourceJson==rich.SourceJson)&&after.Notes.Single().AttachmentIds.Length==0,"Historical-only immutable object and exact opaque SourceJson survive without adding current access");
        byte[] actual=AttachmentObjectCodec.Decrypt(after.AttachmentObjects.Single(o=>o.ObjectId==obj.ObjectId),f.Session.VaultIdentity,after.AttachmentRootId,rootKey);try{Require(actual.SequenceEqual(objectBytes),"Imported history-only encrypted object authenticates with actual current root key");}finally{CryptographicOperations.ZeroMemory(actual);}
        byte[] alternate=RandomNumberGenerator.GetBytes(32);try
        {
            var forged=AttachmentObjectCodec.Encrypt(objectBytes,f.Session.VaultIdentity,current.AttachmentRootId,alternate,"different-key.bin","application/octet-stream");var wrong=head with{RevisionId=Guid.NewGuid(),AttachmentIds=[forged.ObjectId],Parents=[f.Ancestor.RevisionId]};
            f.ReplaceSource(new(5,current.DeviceId,[wrong]){History=[f.AncestorRevision],AttachmentRootId=current.AttachmentRootId,AttachmentObjects=[forged]},alternate);
            byte[] before=SnapshotSerialization.Bytes(f.Session.Workspace.Capture());bool refused=false;try{await f.Preview(view);}catch(CryptographicException){refused=true;}
            Require(refused&&before.SequenceEqual(SnapshotSerialization.Bytes(f.Session.Workspace.Capture())),"Valid same-vault/same-root-ID alternate-key backup rejects before any mutation");
        }
        finally{CryptographicOperations.ZeroMemory(alternate);}
        Console.WriteLine("PASS: O06 historical-only attachment/exact opaque document preservation and actual current-root-key rejection");
    }
    private sealed class Fixture:IDisposable
    {
        internal readonly string DirectoryPath,SourcePath,Root;
        internal readonly byte[] Secret;
        internal readonly EncryptedVault Vault;
        internal readonly SaveCoordinator Session;
        internal readonly StoredNote Ancestor,Branch;
        internal readonly StoredRevision AncestorRevision;
        internal byte[] Cipher=[];
        internal Fixture(IAtomicVaultFiles? files=null)
        {
            DirectoryPath=Path.Combine(Path.GetTempPath(),"memo-o06-lifecycle-"+Guid.NewGuid().ToString("N"));Root=Path.Combine(DirectoryPath,"vault");SourcePath=Path.Combine(DirectoryPath,"source.vault");Secret=EncryptedVault.GenerateRecoverySecret();var now=DateTimeOffset.UnixEpoch;
            Ancestor=new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"A","A");AncestorRevision=new(Ancestor.NoteId,Ancestor.RevisionId,[],now,"A","A");
            var current=Ancestor with{RevisionId=Guid.NewGuid(),Parents=[Ancestor.RevisionId],Title="B",Text="B"};Branch=Ancestor with{RevisionId=Guid.NewGuid(),Parents=[Ancestor.RevisionId],Title="C",Text="C"};var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[current]){History=[AncestorRevision]};
            using(var seed=EncryptedVault.Create(Root,Secret,Secret))seed.Save(snapshot);Vault=EncryptedVault.Open(Root,Secret,files:files);Session=new(Vault,TimeProvider.System);ReplaceSource(snapshot with{Notes=[Branch]},null);
        }
        internal void ReplaceSource(VaultSnapshot snapshot,byte[]? rootKey)
        {
            CryptographicOperations.ZeroMemory(Cipher);byte[] plain=SnapshotSerialization.Bytes(snapshot),key=RandomNumberGenerator.GetBytes(32);
            try{var header=new EnvelopeHeader(Session.VaultIdentity,Guid.NewGuid(),Guid.NewGuid(),1,rootKey is null?2UL:3UL,plain.Length);Cipher=rootKey is null?VaultEnvelope.Encrypt(plain,key,Secret,header):AttachmentEnvelope.Encrypt(plain,key,Secret,header,snapshot.AttachmentRootId,rootKey);File.WriteAllBytes(SourcePath,Cipher);}
            finally{CryptographicOperations.ZeroMemory(plain);CryptographicOperations.ZeroMemory(key);}
        }
        internal Task<BackupMergePreview> Preview(BackupMergeViewAuthority view)=>Session.PreviewEncryptedBackupMergeAsync(Cipher,[Branch.NoteId],SourcePath,Session.AttachmentPreviewEpoch,view);
        internal async Task<ConfirmedBackupMergeToken> Confirm(BackupMergeViewAuthority view){var p=await Preview(view);view.MarkDisplayed(p.Token);return Session.ConfirmBackupMerge(p.Token);}
        public void Dispose(){Session.LockAsync().GetAwaiter().GetResult();Session.Dispose();CryptographicOperations.ZeroMemory(Secret);CryptographicOperations.ZeroMemory(Cipher);System.IO.Directory.Delete(DirectoryPath,true);}
    }
    private sealed class FailingRecovery:IAtomicVaultFiles
    {public Stream CreateNew(string path)=>throw new IOException("synthetic checkpoint create fault");public void FlushToDisk(Stream s)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();public void Move(string a,string b)=>throw new NotSupportedException();}
    private sealed class BlockedRecovery:IAtomicVaultFiles,IDisposable
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);internal readonly ManualResetEventSlim Release=new();private readonly AtomicVaultFiles real=new();
        public Stream CreateNew(string path){Entered.TrySetResult();if(!Release.Wait(TimeSpan.FromSeconds(10)))throw new IOException("synthetic block timeout");return real.CreateNew(path);}
        public void FlushToDisk(Stream s)=>real.FlushToDisk(s);public void Replace(string a,string b,string c)=>real.Replace(a,b,c);public void Move(string a,string b)=>real.Move(a,b);public void Dispose(){Release.Set();Release.Dispose();}
    }
    private static void Require(bool value,string message)=>VaultChecks.Require(value,message);
}

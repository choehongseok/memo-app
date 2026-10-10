using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class BranchResolutionLifecycleChecks
{
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(SaveCoordinator).GetMethod("CreateBranchResolutionView") is not null,"Closed-owner causal resolution API missing");
        await Happy();await Authority();await CopyFailures();await ObserverAndCommit();await RootAuthority();WrongOwner();
        Console.WriteLine("PASS: O06 keep-current owner authority, ciphertext preservation, settlement and durable outcomes");
    }
    private static async Task Happy()
    {
        using var f=new Fixture();var note=f.Note;note.Title="dirty synthetic B";
        using var view=f.View();var preview=await f.Preview(view);VaultChecks.Require(!f.Owner.IsDirty&&preview.Comparison.Current.Title==note.Title,"Dirty checkpoint before fresh clean branch preview");
        byte[] committed=File.ReadAllBytes(f.CurrentPath);view.MarkDisplayed(preview.Token);var confirmed=f.Owner.ConfirmBranchResolution(preview.Token);
        var result=await f.Owner.ResolvePendingBranchAsync(confirmed);var after=f.Owner.Workspace.Capture();var head=after.Notes.Single();
        VaultChecks.Require(result.Disposition==BranchResolutionDisposition.AppliedAndSaved&&head.Title=="dirty synthetic B"&&head.Text=="B exact\r\n e\u0301"&&head.Parents.SequenceEqual(new[]{preview.CurrentRevisionId,f.C.RevisionId})&&result.NewRevisionId==head.RevisionId&&result.RemainingTips.SequenceEqual(new[]{f.D.RevisionId}),"Keep current exact content with explicit ordered two parents and unrelated pending tip");
        VaultChecks.Require(Directory.GetFiles(f.Root,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(committed)),"Fresh encrypted preservation exactly equals clean prechange current cipher");
        VaultChecks.Require((await f.Owner.ResolvePendingBranchAsync(confirmed)).Disposition==BranchResolutionDisposition.NotApplied&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==head.RevisionId,"Reuse never creates another causal revision");
        await f.Owner.LockAsync();f.Owner.Dispose();f.Disposed=true;using var reopened=EncryptedVault.Open(f.Root,f.Secret);
        VaultChecks.Require(reopened.Loaded.Notes.Single().RevisionId==head.RevisionId&&reopened.Loaded.History.Any(r=>r.RevisionId==f.C.RevisionId)&&reopened.Loaded.History.Any(r=>r.RevisionId==preview.CurrentRevisionId),"Encrypted reopen retains exact current and both predecessors without external backup file");
    }
    private static async Task Authority()
    {
        using(var f=new Fixture())
        {
            bool rejected=false;using var view=f.Owner.CreateBranchResolutionView(()=>{if(!rejected){VaultChecks.ExpectFailure(()=>f.Owner.Dispose(),"Recovery owner cannot dispose during synchronous preview callback");rejected=true;}return true;},()=>true);var p=await f.Preview(view);
            VaultChecks.Require(rejected&&!f.Owner.KeysReleased&&p.Token.Prepared is not null,"Shared recovery guard keeps owner live across preview callback");
        }
        using(var f=new Fixture())
        {
            f.Note.Title+=" checkpoint dirty";using var view=f.View();bool rejected=false;Action callback=()=>{if(!rejected&&view.IsCheckpointActive){VaultChecks.ExpectFailure(()=>f.Owner.Dispose(),"Dispose cannot release writer during checkpoint status callback before tail reservation");rejected=true;}};f.Owner.Changed+=callback;
            BranchResolutionPreview p;try{p=await f.Preview(view);}finally{f.Owner.Changed-=callback;}
            VaultChecks.Require(rejected&&!f.Owner.IsDirty&&!f.Owner.KeysReleased&&p.Token.Prepared is not null,"Checkpoint callback disposal refusal preserves writer then fresh clean preview");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var p=await f.Preview(view);VaultChecks.ExpectFailure(()=>f.Owner.ConfirmBranchResolution(p.Token),"Undisplayed token cannot confirm");
            using var other=f.View();VaultChecks.ExpectFailure(()=>other.MarkDisplayed(p.Token),"Wrong host cannot display genuine token");view.MarkDisplayed(p.Token);var c=f.Owner.ConfirmBranchResolution(p.Token);
            VaultChecks.ExpectFailure(()=>f.Owner.ConfirmBranchResolution(p.Token),"Confirmation is single use");
            using(var foreign=new Fixture())
            {
                VaultChecks.ExpectFailure(()=>foreign.Owner.ResolvePendingBranchAsync(c),"Unbound coordinator cannot enter owner recovery APIs");using var foreignView=foreign.View();
                VaultChecks.Require((await foreign.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied&&p.Token.Prepared is not null,"Foreign coordinator cannot consume original owner authority");
            }
            f.Note.Text+=" changed";VaultChecks.Require(p.Token.Prepared is null,"Idle edit releases candidate");VaultChecks.Require((await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied&&f.Owner.IsDirty,"Dirty after preview never silently checkpoints confirmed source");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var p=await f.Preview(view);view.MarkDisplayed(p.Token);var c=f.Owner.ConfirmBranchResolution(p.Token);f.Owner.Workspace.AcceptPrepared(f.Owner.Workspace.Capture());
            VaultChecks.Require(p.Token.Prepared is null&&(await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied,"Same-version acceptance revokes confirmation");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var p=await f.Preview(view);view.MarkDisplayed(p.Token);var c=f.Owner.ConfirmBranchResolution(p.Token);view.Dispose();
            VaultChecks.Require(p.Token.Prepared is null&&(await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied,"Selection or host disposal revokes exact token");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var p=await f.Preview(view);view.MarkDisplayed(p.Token);var c=f.Owner.ConfirmBranchResolution(p.Token);using var next=f.View();await f.Preview(next);
            VaultChecks.Require(p.Token.Prepared is null&&(await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied,"Repreview cannot rebind selected tip authority");
        }
        using(var f=new Fixture())
        {
            BranchResolutionPreviewToken? failed=null;using var view=f.Owner.CreateBranchResolutionView(()=>{failed=(BranchResolutionPreviewToken?)typeof(SaveCoordinator).GetField("branchPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Owner);if(failed is not null)throw new InvalidOperationException("synthetic final preview callback");return true;},()=>true);
            bool refused=false;try{await f.Preview(view);}catch(InvalidOperationException){refused=true;}
            VaultChecks.Require(refused&&failed is {Prepared:null},"Failed final preview callback clears newly registered candidate");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var p=await f.Preview(view);view.MarkDisplayed(p.Token);var c=f.Owner.ConfirmBranchResolution(p.Token);await f.Owner.LockAsync();f.Owner.Dispose();f.Disposed=true;
            using var reopened=new SaveCoordinator(EncryptedVault.Open(f.Root,f.Secret),TimeProvider.System);using var reopenedView=reopened.CreateBranchResolutionView(()=>true,()=>true);
            VaultChecks.Require(reopened.Workspace.Notes.Single().Id==f.A.NoteId&&(await reopened.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied,"Same vault and note IDs after reopen cannot replay old owner nonce/token");
        }
        using(var f=new Fixture(new VaultFailureChecks.FaultFiles("pre-flush")))
        {
            f.Note.Title+=" dirty";using var view=f.View();bool refused=false;try{await f.Preview(view);}catch(InvalidOperationException){refused=true;}
            VaultChecks.Require(refused&&f.Owner.IsDirty&&Directory.GetFiles(f.Root,"previous-*.vault").Length==0,"Checkpoint failure retains dirty edits without preview or prechange write");
        }
    }
    private static async Task CopyFailures()
    {
        foreach(string failure in new[]{"create","flush","readback"})
        {
            using var f=new Fixture();using var view=f.View();var c=await f.Confirm(view);byte[] before=SnapshotSerialization.Bytes(f.Owner.Workspace.Capture());
            var result=await f.Owner.StartBranchResolution(c,new CopyFault(failure));VaultChecks.Require(result.Disposition==BranchResolutionDisposition.NotApplied&&before.SequenceEqual(SnapshotSerialization.Bytes(f.Owner.Workspace.Capture())),"Preservation "+failure+" fails before any candidate field change");
            VaultChecks.Require((await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied,"Failed apply consumes confirmed token");
        }
        foreach(string revoke in new[]{"host","cipher","lock","lock-backup"})
        {
            using var f=new Fixture();using var view=f.View();var c=await f.Confirm(view);byte[] before=SnapshotSerialization.Bytes(f.Owner.Workspace.Capture()),original=File.ReadAllBytes(f.CurrentPath);
            using var blocked=new BlockedCopy();Task<BranchResolutionResult>? running=null;Task<bool>? locking=null;
            try
            {
                running=f.Owner.StartBranchResolution(c,blocked);await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                VaultChecks.Require(!await f.Owner.BackupAsync(Path.Combine(f.DirectoryPath,"overlap.vault")),"Concurrent backup rejected while preservation owns admission");
                using var nested=f.View();bool rejected=false;try{await f.Preview(nested);}catch(InvalidOperationException){rejected=true;}VaultChecks.Require(rejected,"Nested resolution preview rejected");
                if(revoke=="host")view.Dispose();else if(revoke=="cipher")File.WriteAllBytes(f.CurrentPath,[1,2,3]);else{locking=revoke=="lock-backup"?f.Owner.LockWithBackupAsync(Path.Combine(f.DirectoryPath,"locked-copy.vault")):f.Owner.LockAsync();VaultChecks.Require(f.Owner.IsLocked&&f.Owner.KeysReleased&&!locking.IsCompleted,"Lock conceals/releases key authority before slow copy actually settles");}
                blocked.Release.Set();var result=await running;
                VaultChecks.Require(result.Disposition==BranchResolutionDisposition.NotApplied,$"Late copy refuses stale publication ({revoke}: {result.Disposition})");
                if(locking is not null){await locking;if(revoke=="lock-backup")VaultChecks.Require(File.Exists(Path.Combine(f.DirectoryPath,"locked-copy.vault")),"LockWithBackup chains detached backup after genuine prior copy settlement");}else VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(f.Owner.Workspace.Capture())),"Revocation leaves whole workspace unchanged");
                VaultChecks.Require(!f.Owner.IsBusy,$"All actual copy and lock-backup work settles ({revoke})");
                VaultChecks.Require(Directory.GetFiles(f.Root,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(original)),"Successfully preserved late copy retained after refusal");
            }
            finally
            {
                blocked.Release.Set();try{if(running is not null)await running;}finally{if(locking is not null)await locking;}
                if(revoke=="cipher")File.WriteAllBytes(f.CurrentPath,original);
            }
        }
        foreach(bool throws in new[]{false,true})
        {
            using var f=new Fixture();bool afterAwait=false;using var view=f.Owner.CreateBranchResolutionView(()=>{if(afterAwait){afterAwait=false;if(throws)throw new InvalidOperationException("synthetic host callback after copy");f.Note.Title+=" callback edit";}return true;},()=>true);var c=await f.Confirm(view);Guid incoming=c.Preview.Prepared!.NewRevisionId;
            using var blocked=new BlockedCopy();Task<BranchResolutionResult>? task=null;
            try
            {
                task=f.Owner.StartBranchResolution(c,blocked);await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));afterAwait=true;blocked.Release.Set();var result=await task;
                VaultChecks.Require(result.Disposition==BranchResolutionDisposition.NotApplied&&f.Owner.Workspace.Capture().Notes.All(n=>n.RevisionId!=incoming),"Throwing or mutating host predicate after preservation cannot publish stale candidate");
            }
            finally{blocked.Release.Set();if(task is not null)await task;}
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var c=await f.Confirm(view);byte[] original=File.ReadAllBytes(f.CurrentPath);File.WriteAllBytes(f.CurrentPath,[1]);
            VaultChecks.Require((await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied&&Directory.GetFiles(f.Root,"previous-*.vault").Length==0,"Changed current cipher detected before preservation");File.WriteAllBytes(f.CurrentPath,original);
        }
    }
    private static async Task ObserverAndCommit()
    {
        using(var f=new Fixture())
        {
            using var view=f.View();var c=await f.Confirm(view);Action failure=()=>throw new InvalidOperationException("synthetic observer after fields");f.Owner.Changed+=failure;
            BranchResolutionResult result;try{result=await f.Owner.ResolvePendingBranchAsync(c);}finally{f.Owner.Changed-=failure;}
            VaultChecks.Require(result.Disposition==BranchResolutionDisposition.AppliedDirty&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==result.NewRevisionId&&f.Owner.IsDirty,"Observer throw reports complete candidate AppliedDirty");
            VaultChecks.Require(await f.Owner.SaveAsync()&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==result.NewRevisionId,"Retry save keeps original causal revision ID");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var c=await f.Confirm(view);Task<bool>? locking=null;Action lockObserver=()=>locking??=f.Owner.LockAsync();f.Owner.Changed+=lockObserver;
            BranchResolutionResult result;try{result=await f.Owner.ResolvePendingBranchAsync(c);}finally{f.Owner.Changed-=lockObserver;}
            VaultChecks.Require(result.Disposition==BranchResolutionDisposition.AppliedDirty&&locking is not null,"Observer lock after complete field swap reports AppliedDirty");await locking!;f.Owner.Dispose();f.Disposed=true;using var reopened=EncryptedVault.Open(f.Root,f.Secret);VaultChecks.Require(reopened.Loaded.Notes.Single().RevisionId==result.NewRevisionId,"Observer lock encryption retains same two-parent revision after reopen");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var c=await f.Confirm(view);byte[] original=File.ReadAllBytes(f.CurrentPath);bool replaced=false;Action replaceAfterFields=()=>{if(replaced)return;replaced=true;File.WriteAllBytes(f.CurrentPath,"public synthetic post-publication replacement"u8.ToArray());};f.Owner.Changed+=replaceAfterFields;
            BranchResolutionResult result;try{result=await f.Owner.ResolvePendingBranchAsync(c);}finally{f.Owner.Changed-=replaceAfterFields;}
            VaultChecks.Require(replaced&&result.Disposition==BranchResolutionDisposition.AppliedDirty&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==result.NewRevisionId&&File.ReadAllBytes(f.CurrentPath).SequenceEqual("public synthetic post-publication replacement"u8.ToArray()),"Expected-base commit guard preserves changed ciphertext and reports complete in-memory candidate AppliedDirty");File.WriteAllBytes(f.CurrentPath,original);
        }
        using(var f=new Fixture(new VaultFailureChecks.FaultFiles("pre-flush")))
        {
            using var view=f.View();var c=await f.Confirm(view);var result=await f.Owner.ResolvePendingBranchAsync(c);
            VaultChecks.Require(result.Disposition==BranchResolutionDisposition.AppliedDirty&&f.Owner.IsDirty&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==result.NewRevisionId,"Commit failure retains complete applied candidate as dirty");
        }
        using(var f=new Fixture())
        {
            using var view=f.View();var c=await f.Confirm(view);Action<NoteDraft?> retire=_=>view.Dispose();f.Owner.Workspace.AttachmentReadInvalidating+=retire;
            try{VaultChecks.Require((await f.Owner.ResolvePendingBranchAsync(c)).Disposition==BranchResolutionDisposition.NotApplied&&f.Owner.Workspace.Capture().Notes.Single().RevisionId==f.B.RevisionId,"Host native retirement during invalidation aborts before field swap");}finally{f.Owner.Workspace.AttachmentReadInvalidating-=retire;}
        }
    }
    private static async Task RootAuthority()
    {
        using var f=new Fixture(wrongRootObject:true);using var view=f.View();byte[] before=SnapshotSerialization.Bytes(f.Owner.Workspace.Capture());bool refused=false;
        try{await f.Preview(view);}catch(CryptographicException){refused=true;}
        VaultChecks.Require(refused&&before.SequenceEqual(SnapshotSerialization.Bytes(f.Owner.Workspace.Capture()))&&Directory.GetFiles(f.Root,"previous-*.vault").Length==0,"Valid authenticated current vault rejects deliberately forged same-root-ID wrong-key in-memory historical candidate before preview");
    }
    private static void WrongOwner()
    {
        using var f=new Fixture();int owner=Environment.CurrentManagedThreadId;using var view=f.Owner.CreateBranchResolutionView(()=>true,()=>Environment.CurrentManagedThreadId==owner);var p=f.Preview(view).GetAwaiter().GetResult();Exception? error=null;int foreign=0;
        var thread=new Thread(()=>{foreign=Environment.CurrentManagedThreadId;try{view.MarkDisplayed(p.Token);}catch(Exception e){error=e;}});thread.Start();thread.Join();VaultChecks.Require(foreign!=owner&&error is InvalidOperationException&&view.Displayed is null,"Foreign owner cannot mutate displayed authority");
    }
    private sealed class Fixture:IDisposable
    {
        internal readonly string DirectoryPath,Root;internal string CurrentPath=>Path.Combine(Root,"current.vault");internal readonly byte[] Secret;internal readonly SaveCoordinator Owner;internal readonly StoredNote A,B;internal readonly StoredRevision C,D;
        internal bool Disposed;internal NoteDraft Note=>Owner.Workspace.Notes.Single();
        internal Fixture(IAtomicVaultFiles? files=null,bool wrongRootObject=false)
        {
            DirectoryPath=Path.Combine(Path.GetTempPath(),"memo-resolution-owner-"+Guid.NewGuid().ToString("N"));Root=Path.Combine(DirectoryPath,"vault");Secret=EncryptedVault.GenerateRecoverySecret();var now=DateTimeOffset.UnixEpoch;
            SaveCoordinator? opened=null;
            try
            {
                A=new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"A","A");B=A with{RevisionId=Guid.NewGuid(),Parents=[A.RevisionId],Title="B",Text="B exact\r\n e\u0301"};C=new(A.NoteId,Guid.NewGuid(),[A.RevisionId],now,"C","C retained");D=C with{RevisionId=Guid.NewGuid(),Title="D",Text="D retained"};
                var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[B]){History=[new(A.NoteId,A.RevisionId,[],now,"A","A"),C,D]};Guid vaultId;using(var seed=EncryptedVault.Create(Root,Secret,Secret)){seed.Save(snapshot);vaultId=seed.Identity;}
                VaultSnapshot? forgedBasis=null;
                if(wrongRootObject)
                {
                    Guid rootId=Guid.NewGuid();byte[] rootKey=RandomNumberGenerator.GetBytes(32),otherKey=RandomNumberGenerator.GetBytes(32),contentKey=RandomNumberGenerator.GetBytes(32);byte[]? validCipher=null,badCipher=null;
                    byte[] Encrypt(VaultSnapshot value)
                    {
                        byte[] plaintext=SnapshotSerialization.Bytes(value);
                        try{return AttachmentEnvelope.Encrypt(plaintext,contentKey,Secret,new(vaultId,Guid.NewGuid(),Guid.NewGuid(),1,3UL,plaintext.Length),rootId,rootKey);}
                        finally{CryptographicOperations.ZeroMemory(plaintext);}
                    }
                    try
                    {
                        var valid=AttachmentObjectCodec.Encrypt("public synthetic valid-root-key"u8,vaultId,rootId,rootKey,"valid-synthetic.bin","application/octet-stream");
                        var wrong=AttachmentObjectCodec.Encrypt("public synthetic wrong-root-key"u8,vaultId,rootId,otherKey,"wrong-synthetic.bin","application/octet-stream");
                        C=C with{AttachmentIds=[valid.ObjectId]};snapshot=snapshot with{SchemaVersion=5,AttachmentRootId=rootId,AttachmentObjects=[valid],History=[snapshot.History[0],C,D]};
                        forgedBasis=snapshot with{AttachmentObjects=[valid,wrong],History=[snapshot.History[0],C with{AttachmentIds=[wrong.ObjectId]},D]};
                        validCipher=Encrypt(snapshot);badCipher=Encrypt(forgedBasis);File.WriteAllBytes(CurrentPath,badCipher);
                        VaultChecks.ExpectFailure(()=>EncryptedVault.Open(Root,Secret).Dispose(),"Actual encrypted open authenticates and refuses same-root-ID wrong-key object");
                        File.WriteAllBytes(CurrentPath,validCipher);
                    }
                    finally{if(validCipher is not null)CryptographicOperations.ZeroMemory(validCipher);if(badCipher is not null)CryptographicOperations.ZeroMemory(badCipher);CryptographicOperations.ZeroMemory(rootKey);CryptographicOperations.ZeroMemory(otherKey);CryptographicOperations.ZeroMemory(contentKey);}
                }
                opened=Owner=new(EncryptedVault.Open(Root,Secret,files:files),TimeProvider.System);
                // Deliberate structurally valid in-memory test seam. The opened/committed ciphertext remains genuine and valid.
                if(forgedBasis is not null)Owner.Workspace.AcceptPrepared(forgedBasis);
            }
            catch
            {
                bool settled=opened is null;
                try{if(opened is not null){opened.Dispose();settled=true;}}
                finally{CryptographicOperations.ZeroMemory(Secret);if(settled&&Directory.Exists(DirectoryPath))Directory.Delete(DirectoryPath,true);}
                throw;
            }
        }
        internal BranchResolutionViewAuthority View()=>Owner.CreateBranchResolutionView(()=>true,()=>true);
        internal Task<BranchResolutionPreview> Preview(BranchResolutionViewAuthority view)=>Owner.PreviewBranchResolutionAsync(A.NoteId,C.RevisionId,Owner.AttachmentPreviewEpoch,view);
        internal async Task<ConfirmedBranchResolutionToken> Confirm(BranchResolutionViewAuthority view){var p=await Preview(view);view.MarkDisplayed(p.Token);return Owner.ConfirmBranchResolution(p.Token);}
        public void Dispose()
        {
            try{if(!Disposed){Owner.LockAsync().GetAwaiter().GetResult();Owner.Dispose();Disposed=true;}}
            finally{CryptographicOperations.ZeroMemory(Secret);if(Disposed&&!Owner.IsBusy&&Directory.Exists(DirectoryPath))Directory.Delete(DirectoryPath,true);}
        }
    }
    private sealed class CopyFault(string phase):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real=new();
        public Stream CreateNew(string destination){if(phase=="create")throw new IOException("synthetic copy create failure");return real.CreateNew(destination);}
        public void FlushToDisk(Stream stream){if(phase=="flush")throw new IOException("synthetic flush failure");real.FlushToDisk(stream);if(phase=="readback"){long length=stream.Length;stream.SetLength(length-1);real.FlushToDisk(stream);}}
        public void Replace(string a,string b,string c)=>throw new NotSupportedException();public void Move(string a,string b)=>throw new NotSupportedException();
    }
    private sealed class BlockedCopy:IAtomicVaultFiles,IDisposable
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);internal readonly ManualResetEventSlim Release=new();private readonly AtomicVaultFiles real=new();
        public Stream CreateNew(string path){Entered.TrySetResult();if(!Release.Wait(TimeSpan.FromSeconds(15)))throw new IOException("synthetic copy block timeout");return real.CreateNew(path);}
        public void FlushToDisk(Stream s)=>real.FlushToDisk(s);public void Replace(string a,string b,string c)=>throw new NotSupportedException();public void Move(string a,string b)=>throw new NotSupportedException();public void Dispose(){Release.Set();Release.Dispose();}
    }
}

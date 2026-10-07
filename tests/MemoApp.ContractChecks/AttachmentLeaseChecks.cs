using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class AttachmentLeaseChecks
{
    private delegate void Reader(ReadOnlySpan<byte> bytes);
    private static bool Consume(IDisposable lease,Reader reader)
    {
        var method=lease.GetType().GetMethod("Consume")!;var callback=Delegate.CreateDelegate(method.GetParameters()[0].ParameterType,reader.Target,reader.Method);
        try{return (bool)method.Invoke(lease,[callback])!;}catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static byte[] Owned(IDisposable lease)=>(byte[])lease.GetType().GetField("bytes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(lease)!;
    private static Task Idle(SaveCoordinator owner)=>(Task)typeof(SaveCoordinator).GetProperty("WhenAttachmentReadsIdle")!.GetValue(owner)!;
    internal static async Task Run()
    {
        var create=typeof(SaveCoordinator).GetMethod("CreateAttachmentReadLease");VaultChecks.Require(create is not null,"Registered revocable single-use attachment byte lease is missing");
        var root=Path.Combine(Path.GetTempPath(),"memo-attachment-lease-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();var body=RandomNumberGenerator.GetBytes(1024);
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();note.Title="synthetic lease note";VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"Lease test genuine durable root");var id=owner.AttachBytes(note,body,"synthetic.bin","application/octet-stream",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"Lease genuine object save");
            var borrow=create!.CreateDelegate<Func<NoteDraft,Guid,long,IDisposable>>(owner);
            IDisposable Borrow()=>borrow(note,id,note.EditVersion);
            string before=JsonSerializer.Serialize(owner.Workspace.Capture());var lease=Borrow();var bytes=Owned(lease);VaultChecks.Require(bytes.SequenceEqual(body),"Owned lease has genuinely authenticated exact original bytes");
            CleanupOwnership(lease);
            VaultChecks.Require(!lease.GetType().GetProperties().Any(p=>p.PropertyType==typeof(byte[])||p.PropertyType.IsByRefLike)&&!lease.GetType().GetMethods().Any(m=>m.ReturnType==typeof(byte[])||m.ReturnType.IsByRefLike),"No public raw-array/span accessor");
            VaultChecks.Require(Consume(lease,span=>VaultChecks.Require(span.SequenceEqual(body),"Borrowed synchronous read exact bytes"))&&bytes.All(b=>b==0)&&Idle(owner).IsCompleted,"Single use zeroes exact original array and drains without disposal");VaultChecks.ExpectFailure(()=>Consume(lease,_=>{}),"Repeated consume cannot borrow again");lease.Dispose();lease.Dispose();VaultChecks.Require(JsonSerializer.Serialize(owner.Workspace.Capture())==before,"Lease never changes original model/cipher/ref/revision");
            lease=Borrow();bytes=Owned(lease);VaultChecks.ExpectFailure(()=>Consume(lease,_=>throw new IOException("synthetic consumer failure")),"Callback exception is propagated after cleanup");VaultChecks.Require(bytes.All(b=>b==0)&&Idle(owner).IsCompleted,"Callback failure zeroes buffer and returns capacity");
            lease=Borrow();bytes=Owned(lease);VaultChecks.Require(Consume(lease,_=>VaultChecks.ExpectFailure(()=>Consume(lease,_=>{}),"Reentrant Consume cannot acquire running bytes"))&&bytes.All(b=>b==0),"Outer exclusive callback succeeds after nested refusal");
            lease=Borrow();bytes=Owned(lease);VaultChecks.Require(!Consume(lease,span=>{lease.Dispose();VaultChecks.Require(span.SequenceEqual(body),"Self-dispose defers zero until exclusive callback exits");})&&bytes.All(b=>b==0),"Self-dispose returns false then exact zero");
            var first=Borrow();var second=Borrow();VaultChecks.ExpectFailure(()=>Borrow().Dispose(),"Two outstanding grants reserve capacity before a third decrypt");first.Dispose();second.Dispose();VaultChecks.Require(Idle(owner).IsCompleted,"Unused disposed leases drain");
            lease=Borrow();bytes=Owned(lease);bool observed=false;System.ComponentModel.PropertyChangedEventHandler notification=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.Title)){observed=true;VaultChecks.ExpectFailure(()=>Consume(lease,_=>{}),"First public staged-note notification cannot consume old grant");VaultChecks.Require(bytes.All(b=>b==0),"Ready grant already zero before public note callback");}};
            note.PropertyChanged+=notification;owner.Workspace.SetTags(note,["synthetic"]);note.PropertyChanged-=notification;VaultChecks.Require(observed&&Idle(owner).IsCompleted,"Trusted early source invalidation precedes staged Title publication");
            lease=Borrow();bytes=Owned(lease);var captured=owner.Workspace.Capture();long version=note.EditVersion;owner.Workspace.AcceptPrepared(captured with{AttachmentObjects=[]});VaultChecks.Require(note.EditVersion==version&&bytes.All(b=>b==0),"Accepted object replacement revokes without requiring a note version change");VaultChecks.ExpectFailure(()=>Consume(lease,_=>{}),"AcceptPrepared bypass cannot revive old bytes");owner.Workspace.AcceptPrepared(captured);
            VaultChecks.ExpectFailure(()=>borrow(note,id,note.EditVersion-1).Dispose(),"Stale version cannot decrypt");VaultChecks.ExpectFailure(()=>borrow(note,Guid.NewGuid(),note.EditVersion).Dispose(),"Unreferenced object cannot decrypt");
            var foreign=new EditingWorkspace(TimeProvider.System).CreateNote();VaultChecks.ExpectFailure(()=>borrow(foreign,id,foreign.EditVersion).Dispose(),"Foreign draft cannot decrypt");
            var copy=owner.Workspace.Duplicate(note);var copied=borrow(copy,id,copy.EditVersion);var copiedBytes=Owned(copied);owner.Workspace.DeleteNotes([copy]);VaultChecks.Require(copiedBytes.All(x=>x==0),"Batch trash revokes captured attachment source");VaultChecks.ExpectFailure(()=>borrow(copy,id,copy.EditVersion).Dispose(),"Trash cannot decrypt");owner.Workspace.RestoreNote(copy);
            copied=borrow(copy,id,copy.EditVersion);copiedBytes=Owned(copied);owner.DetachAttachment(copy,id,copy.EditVersion);VaultChecks.Require(copiedBytes.All(x=>x==0),"Attachment detach revokes ready bytes");VaultChecks.ExpectFailure(()=>borrow(copy,id,copy.EditVersion).Dispose(),"Detached reference cannot decrypt");
            await ActiveSourceRevoke(owner,note,id,body,borrow);
            await ActiveLock(owner,note,id,body,borrow);
            VaultChecks.ExpectFailure(()=>borrow(note,id,note.EditVersion).Dispose(),"Locked/closed note cannot acquire a new lease");
            TrackerCycle();
            PendingReservation();
            await FaultRevocation(root+"-fault",secret,body);
            await HiddenAndDispose(root+"-hidden",secret,body);
            Console.WriteLine("PASS: registered single-use attachment bytes, exact zero/capacity/idle cycles, early source/accepted-object/callback refusal and paused consumer lock without concurrent zero or retained key authority (no decoder/UI)");
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(body);foreach(var path in new[]{root,root+"-fault",root+"-hidden"})if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    private static async Task ActiveSourceRevoke(SaveCoordinator owner,NoteDraft note,Guid id,byte[] body,Func<NoteDraft,Guid,long,IDisposable> borrow)
    {
        using var release=new ManualResetEventSlim();using var startedA=new ManualResetEventSlim();using var startedB=new ManualResetEventSlim();var a=borrow(note,id,note.EditVersion);var b=borrow(note,id,note.EditVersion);var bytesA=Owned(a);var bytesB=Owned(b);
        Task<bool> Start(IDisposable lease,ManualResetEventSlim started)=>Task.Run(()=>Consume(lease,span=>{started.Set();VaultChecks.Require(release.Wait(10000),"Paused source consumer release");VaultChecks.Require(span.SequenceEqual(body),"Source revoke never concurrently zeros active span");}));
        var taskA=Start(a,startedA);var taskB=Start(b,startedB);
        try{VaultChecks.Require(startedA.Wait(5000)&&startedB.Wait(5000),"Both capacity slots running");note.Title="new synthetic source";VaultChecks.ExpectFailure(()=>borrow(note,id,note.EditVersion).Dispose(),"Revoked-running callbacks retain both capacity slots");VaultChecks.Require(!Idle(owner).IsCompleted&&bytesA.SequenceEqual(body)&&bytesB.SequenceEqual(body),"Running revocation is immediate authority loss with deferred byte cleanup");}
        finally{release.Set();}
        VaultChecks.Require(!await taskA&&!await taskB&&bytesA.All(x=>x==0)&&bytesB.All(x=>x==0),"Both stale callbacks finish false and zero");await Idle(owner);a.Dispose();b.Dispose();
    }
    private static void CleanupOwnership(IDisposable lease)
    {
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);var pending=new Queue<object>();pending.Enqueue(lease);
        while(pending.TryDequeue(out var item))
        {
            if(!seen.Add(item))continue;
            VaultChecks.Require(item is not (NoteDraft or EditingWorkspace or SaveCoordinator or EncryptedVault),"Worker-held cleanup graph must not retain live draft/workspace/coordinator/vault key owner");
            if(item is Delegate callback){VaultChecks.Require(callback.Target is null,"Production cleanup graph cannot retain a key-use closure");continue;}
            if(item is System.Collections.IEnumerable collection&&item is not (string or byte[])){foreach(var value in collection)if(value is not null)pending.Enqueue(value);continue;}
            if(item.GetType().Namespace?.StartsWith("MemoApp.Core",StringComparison.Ordinal)!=true)continue;
            foreach(var field in item.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))if(field.GetValue(item) is {} value)pending.Enqueue(value);
        }
    }
    private static async Task ActiveLock(SaveCoordinator owner,NoteDraft note,Guid id,byte[] body,Func<NoteDraft,Guid,long,IDisposable> borrow)
    {
        using var started=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var lease=borrow(note,id,note.EditVersion);var bytes=Owned(lease);var idle=Idle(owner);bool concealed=false;owner.Conceal+=()=>{concealed=true;VaultChecks.Require(!idle.IsCompleted&&bytes.SequenceEqual(body),"Conceal occurs while running byte cleanup is still deferred");};
        var task=Task.Run(()=>Consume(lease,span=>{started.Set();VaultChecks.Require(release.Wait(10000),"Paused lock consumer release");VaultChecks.Require(span.SequenceEqual(body),"Lock cannot mutate bytes under running reader");}));
        try{VaultChecks.Require(started.Wait(5000),"Running lock consumer");VaultChecks.Require(await owner.LockAsync()&&concealed&&owner.KeysReleased&&!owner.IsBusy&&!idle.IsCompleted,"Lock and cryptographic key release complete before read-idle cleanup");}
        finally{release.Set();}
        VaultChecks.Require(!await task&&bytes.All(x=>x==0),"Locked callback completion refuses stale success and zeroes exact array");await idle;lease.Dispose();
    }
    private static void TrackerCycle()
    {
        var type=typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Editing.AttachmentReadTracker");VaultChecks.Require(type is not null,"Cleanup-only tracker is required");using var captured=new ManualResetEventSlim();using var release=new ManualResetEventSlim();bool armed=true;
        Action hook=()=>{if(armed){armed=false;captured.Set();VaultChecks.Require(release.Wait(10000),"Drain completion fixture release");}};
        var tracker=Activator.CreateInstance(type!,BindingFlags.NonPublic|BindingFlags.Instance,null,[hook],null)!;var reserve=type!.GetMethod("Reserve",BindingFlags.NonPublic|BindingFlags.Instance)!;var when=type.GetProperty("WhenIdle",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var slot=reserve.Invoke(tracker,[new object()])!;var old=(Task)when.GetValue(tracker)!;var finish=slot.GetType().GetMethod("Finish",BindingFlags.NonPublic|BindingFlags.Instance)!;var cleanup=Task.Run(()=>finish.Invoke(slot,null));
        try{VaultChecks.Require(captured.Wait(5000),"Old cycle captured before completion");var next=reserve.Invoke(tracker,[new object()])!;var current=(Task)when.GetValue(tracker)!;VaultChecks.Require(!ReferenceEquals(old,current)&&!current.IsCompleted,"New admission creates a distinct drain cycle");release.Set();cleanup.GetAwaiter().GetResult();VaultChecks.Require(old.IsCompleted&&!current.IsCompleted,"Old completion cannot complete new active cycle");finish.Invoke(next,null);VaultChecks.Require(current.IsCompleted,"Exact new cycle drains only on its last cleanup");}
        finally{release.Set();cleanup.GetAwaiter().GetResult();}
    }
    private static void PendingReservation()
    {
        var type=typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Editing.AttachmentReadTracker")!;var tracker=Activator.CreateInstance(type,BindingFlags.NonPublic|BindingFlags.Instance,null,[null],null)!;var source=new object();
        var slot=type.GetMethod("Reserve",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(tracker,[source])!;var idle=(Task)type.GetProperty("WhenIdle",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(tracker)!;
        type.GetMethod("Revoke",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(tracker,[source]);byte[] bytes=[1,2,3];var leaseType=typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Editing.AttachmentReadLease")!;
        using var lease=(IDisposable)Activator.CreateInstance(leaseType,BindingFlags.NonPublic|BindingFlags.Instance,null,[bytes,slot],null)!;VaultChecks.Require(!(bool)slot.GetType().GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(slot,[lease])!,"Pending reservation keeps irreversible revocation through binding");lease.Dispose();VaultChecks.Require(bytes.All(x=>x==0)&&idle.IsCompleted,"Canceled pending transfer zeroes exact owned bytes and returns capacity");
    }
    private static async Task FaultRevocation(string path,byte[] secret,byte[] body)
    {
        var files=new ToggleFaultFiles();using var owner=new SaveCoordinator(EncryptedVault.Create(path,secret,secret,files),TimeProvider.System);var note=owner.Workspace.CreateNote();VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"Fault fixture root");var id=owner.AttachBytes(note,body,"fault.bin","application/octet-stream",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"Fault fixture baseline");
        IDisposable? lease=null;byte[]? bytes=null;bool observed=false;long version=0;owner.Changed+=()=>{if(owner.Status=="저장 중"){lease=owner.CreateAttachmentReadLease(note,id,note.EditVersion);bytes=Owned(lease);version=note.EditVersion;}if(owner.Status.StartsWith("저장 실패",StringComparison.Ordinal)){observed=true;VaultChecks.Require(note.EditVersion==version&&bytes!.All(x=>x==0),"Commit fault revokes before status callbacks without source edit");VaultChecks.ExpectFailure(()=>Consume(lease!,_=>{}),"Fault cannot leave old grant consumable");}};
        note.Title="synthetic fault";files.Fail=true;VaultChecks.Require(!await owner.SaveAsync()&&observed&&Idle(owner).IsCompleted,"Actual flush fault drains ready read before public notification");VaultChecks.ExpectFailure(()=>owner.CreateAttachmentReadLease(note,id,note.EditVersion).Dispose(),"Faulted owner cannot create grant");lease?.Dispose();
    }
    private static async Task HiddenAndDispose(string path,byte[] secret,byte[] body)
    {
        var owner=new SaveCoordinator(EncryptedVault.Create(path,secret,secret),TimeProvider.System);try
        {
            var note=owner.Workspace.CreateNote();VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"Hidden fixture root");var id=owner.AttachBytes(note,body,"hidden.bin","application/octet-stream",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"Hidden fixture baseline");var old=owner.CreateAttachmentReadLease(note,id,note.EditVersion);var oldBytes=Owned(old);
            note.Title=new string('z',257);VaultChecks.Require(!await owner.LockAsync()&&owner.PendingKind=="plaintext-hidden"&&!owner.KeysReleased&&oldBytes.All(x=>x==0),"Hidden recovery keeps keys only with all old read grants revoked");owner.ResumeHidden(secret);VaultChecks.ExpectFailure(()=>Consume(old,_=>{}),"Hidden/new session never revives old grants");VaultChecks.ExpectFailure(()=>owner.CreateAttachmentReadLease(note,id,note.EditVersion).Dispose(),"Old draft cannot grant after hidden recovery");note=owner.Workspace.Notes.Single();note.Title="repaired synthetic";VaultChecks.Require(await owner.SaveAsync(),"Hidden fixture repair");
            var ready=owner.CreateAttachmentReadLease(note,id,note.EditVersion);var readyBytes=Owned(ready);owner.Workspace.Clear();VaultChecks.Require(readyBytes.All(x=>x==0)&&Idle(owner).IsCompleted,"Direct Workspace.Clear revokes before public close");owner.Dispose();
        }
        finally{owner.Dispose();}
        owner=new SaveCoordinator(EncryptedVault.Open(path,secret),TimeProvider.System);var activeNote=owner.Workspace.Notes.Single();var activeId=activeNote.AttachmentIds.Single();var active=owner.CreateAttachmentReadLease(activeNote,activeId,activeNote.EditVersion);var activeBytes=Owned(active);using var started=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var task=Task.Run(()=>Consume(active,span=>{started.Set();VaultChecks.Require(release.Wait(10000)&&span.SequenceEqual(body),"Dispose defers running input zero");}));
        try{VaultChecks.Require(started.Wait(5000),"Dispose fixture consumer started");activeNote.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.IsClosed))throw new IOException("Synthetic native close exception");};VaultChecks.ExpectFailure(()=>owner.Dispose(),"Close callback failure remains observable");VaultChecks.Require(owner.KeysReleased&&!Idle(owner).IsCompleted&&activeBytes.SequenceEqual(body),"Close failure still releases keys without waiting for read cleanup");}
        finally{release.Set();}
        VaultChecks.Require(!await task&&activeBytes.All(x=>x==0),"Disposed running read cannot report successful authority");await Idle(owner);active.Dispose();owner.Dispose();
    }
    private sealed class ToggleFaultFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool Fail;
        public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic lease flush fault");actual.FlushToDisk(stream);}public void Replace(string temp,string current,string previous)=>actual.Replace(temp,current,previous);public void Move(string temp,string current)=>actual.Move(temp,current);
    }
}

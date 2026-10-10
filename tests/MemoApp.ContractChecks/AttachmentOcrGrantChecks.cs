using System.Reflection;
using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class AttachmentOcrGrantChecks
{
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(SaveCoordinator).GetMethod("CreateAttachmentOcrReadGrant",BindingFlags.Instance|BindingFlags.NonPublic) is not null,"Genuine OCR issuer grant API missing");
        string root=Path.Combine(Path.GetTempPath(),"memo-ocr-grants-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        byte[] png=(byte[])typeof(PngDecoderChecks).GetMethod("Png",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[1,1,4,new byte[][]{[0x78,0x01,0x01,0x05,0x00,0xfa,0xff,0,1,2,3,4,0,0x19,0,0x0b]}])!;
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);
            var note=owner.Workspace.CreateNote();note.Title="합성 OCR source";note.Text="original\r\n body";
            VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"OCR grant anchored root");Guid id=owner.AttachBytes(note,png,"synthetic.png","image/png",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"OCR grant saved source");
            // Serialized owner policy is explicit; genuine foreign-thread rejection is tested separately.
            Func<bool> access=()=>true;
            AttachmentOcrReadGrant Grant()=>owner.CreateAttachmentOcrReadGrant(note,id,note.EditVersion,owner.AttachmentPreviewEpoch,access);
            OwnedOcrDerivation Result(AttachmentOcrReadGrant grant,string text="합성 OCR exact\r\n e\u0301 ",bool wrongModel=false)
            {
                grant.Lease.Dispose(); // Synthetic worker: no engine execution claim.
                var facts=new OcrProvenanceFacts(new('a',64),wrongModel?new('b',64):"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2","7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2",1,1,1,1,new('d',64));
                return(OwnedOcrDerivation)typeof(OwnedOcrDerivation).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke([new OwnedOcrText(Encoding.UTF8.GetBytes(text)),grant.Source,grant.Stamp,facts,default(CancellationToken),null]);
            }
            OwnedOcrDerivation Ready(AttachmentOcrReadGrant grant)
            {var result=Result(grant);Bind(owner,grant,new(Task.FromResult(result),Task.CompletedTask));owner.SettleAttachmentOcrGrant(grant);return result;}
            var slots=new List<AttachmentOcrReadGrant>();for(int i=0;i<4;i++){var grant=Grant();grant.Lease.Dispose();slots.Add(grant);}
            Fail(()=>Grant());VaultChecks.Require(owner.WhenAttachmentReadsIdle.IsCompleted,"Cap refusal performs no authenticated plaintext read");foreach(var grant in slots)owner.RetireAttachmentOcrGrant(grant);
            var running=Grant();var completion=new TaskCompletionSource<OwnedOcrDerivation>();var settled=new TaskCompletionSource();Bind(owner,running,new(completion.Task,settled.Task));owner.RetireAttachmentOcrGrant(running);
            slots.Clear();for(int i=0;i<3;i++){var g=Grant();g.Lease.Dispose();slots.Add(g);}Fail(()=>Grant());settled.SetResult();Fail(()=>owner.SettleAttachmentOcrGrant(running));completion.SetCanceled();owner.SettleAttachmentOcrGrant(running);foreach(var g in slots)owner.RetireAttachmentOcrGrant(g);
            var notStarted=Grant();owner.RetireAttachmentOcrGrant(notStarted);var replacement=Grant();owner.RetireAttachmentOcrGrant(replacement);
            var other=owner.Workspace.Duplicate(note);VaultChecks.Require(other.AttachmentIds.Contains(id),"A to B fixture shares exact immutable source object");var original=Grant();var wrong=Ready(original);
            VaultChecks.Require(owner.ApplyAttachmentOcrResult(other,id,original.Stamp.Version,original.Stamp.PreviewEpoch,wrong)==AttachmentOcrApplyOutcome.Rejected,"A to B attempt rejected and consumed");
            VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,original.Stamp.Version,original.Stamp.PreviewEpoch,wrong)==AttachmentOcrApplyOutcome.Rejected,"Consumed wrong-note result cannot retry A");
            Fail(()=>_=wrong.Text);
            var stale=Grant();var staleResult=Ready(stale);note.Text+=" edit";
            VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,stale.Stamp.Version,stale.Stamp.PreviewEpoch,staleResult)==AttachmentOcrApplyOutcome.Rejected,"Source edit revokes ready grant");Fail(()=>_=staleResult.Text);
            var idle=Grant();note.Title+=" change";Fail(()=>Bind(owner,idle,new(Task.FromException<OwnedOcrDerivation>(new InvalidOperationException("must not start")),Task.CompletedTask)));
            var idleReady=Grant();var idleResult=Ready(idleReady);byte[] idleBytes=Bytes(idleResult);owner.RetireAttachmentOcrGrant(idleReady);VaultChecks.Require(idleBytes.All(b=>b==0),"Explicit retirement immediately clears idle owned result");Fail(()=>_=idleResult.Text);
            var borrowed=Grant();var borrowedResult=Ready(borrowed);byte[] borrowedBytes=Bytes(borrowedResult);bool borrowRetired=borrowedResult.ConsumeUtf8(bytes=>{owner.RetireAttachmentOcrGrant(borrowed);VaultChecks.Require(bytes.IndexOfAnyExcept((byte)0)>=0,"Self retirement does not zero borrowed span until callback finishes");});VaultChecks.Require(!borrowRetired&&borrowedBytes.All(b=>b==0),"Borrowed self retirement revokes and clears after finally");
            var badModel=Grant();var badResult=Result(badModel,wrongModel:true);Bind(owner,badModel,new(Task.FromResult(badResult),Task.CompletedTask));
            VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,badModel.Stamp.Version,badModel.Stamp.PreviewEpoch,badResult)==AttachmentOcrApplyOutcome.Rejected&&note.Metadata.AttachmentOcrResults.IsEmpty,"Unpinned model facts rejected before any metadata mutation");Fail(()=>_=badResult.Text);
            var preThrow=Grant();var preThrowResult=Ready(preThrow);long preThrowVersion=note.EditVersion;
            Action<NoteDraft?> rejectPublication=_=>throw new InvalidOperationException("synthetic pre-publication callback");owner.Workspace.AttachmentReadInvalidating+=rejectPublication;
            try{VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,preThrow.Stamp.Version,preThrow.Stamp.PreviewEpoch,preThrowResult)==AttachmentOcrApplyOutcome.Rejected&&note.EditVersion==preThrowVersion&&note.Metadata.AttachmentOcrResults.IsEmpty,"Invalidation callback failure leaves incoming metadata unapplied");}finally{owner.Workspace.AttachmentReadInvalidating-=rejectPublication;}Fail(()=>_=preThrowResult.Text);
            var reentered=Grant();var reenteredResult=Ready(reentered);long reenteredVersion=note.EditVersion;bool entered=false;
            Action<NoteDraft?> secondEpoch=_=>{if(entered)return;entered=true;owner.Workspace.AcceptPrepared(owner.Workspace.Capture());};owner.Workspace.AttachmentReadInvalidating+=secondEpoch;
            try{VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,reentered.Stamp.Version,reentered.Stamp.PreviewEpoch,reenteredResult)==AttachmentOcrApplyOutcome.Rejected&&entered&&owner.AttachmentPreviewEpoch>=reentered.Stamp.PreviewEpoch+2&&note.EditVersion==reenteredVersion&&note.Metadata.AttachmentOcrResults.IsEmpty,"Second same-version acceptance invalidation revokes narrow publication permit");}finally{owner.Workspace.AttachmentReadInvalidating-=secondEpoch;}Fail(()=>_=reenteredResult.Text);
            var hostRetired=Grant();var hostRetiredResult=Ready(hostRetired);long hostRetiredVersion=note.EditVersion;
            Action<NoteDraft?> retireDuringPublication=_=>owner.RetireAttachmentOcrGrant(hostRetired);owner.Workspace.AttachmentReadInvalidating+=retireDuringPublication;
            try{VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,hostRetired.Stamp.Version,hostRetired.Stamp.PreviewEpoch,hostRetiredResult)==AttachmentOcrApplyOutcome.Rejected&&note.EditVersion==hostRetiredVersion&&note.Metadata.AttachmentOcrResults.IsEmpty,"Native host retirement revokes removed-but-applying permit");}finally{owner.Workspace.AttachmentReadInvalidating-=retireDuringPublication;}Fail(()=>_=hostRetiredResult.Text);
            var good=Grant();var goodResult=Ready(good);string title=note.Title,text=note.Text;var attachments=note.AttachmentIds;long version=note.EditVersion;
            VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,good.Stamp.Version,good.Stamp.PreviewEpoch,goodResult)==AttachmentOcrApplyOutcome.AppliedDirty,"Genuine linked result applies");
            VaultChecks.Require(note.Title==title&&note.Text==text&&note.AttachmentIds.SequenceEqual(attachments)&&note.EditVersion==version+1&&note.Metadata.AttachmentOcrResults.Single().Text=="합성 OCR exact\r\n e\u0301 "&&other.Metadata.AttachmentOcrResults.IsEmpty,"Metadata-only normal revision preserves original and other note");Fail(()=>_=goodResult.Text);
            VaultChecks.Require(await owner.SaveAsync(),"OCR metadata saved");
            id=owner.AttachBytes(note,png,"synthetic-second.png","image/png",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"Second synthetic object saved");
            var observed=Grant();var observedResult=Ready(observed);Action boom=()=>throw new InvalidOperationException("synthetic post-publication observer");owner.Changed+=boom;
            try{VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,observed.Stamp.Version,observed.Stamp.PreviewEpoch,observedResult)==AttachmentOcrApplyOutcome.AppliedDirty,"Changed failure after apply reports applied dirty");}finally{owner.Changed-=boom;}
            var accepted=Grant();var acceptedResult=Ready(accepted);VaultChecks.Require(await owner.SaveAsync(),"Same-version snapshot acceptance revokes grant");VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,accepted.Stamp.Version,accepted.Stamp.PreviewEpoch,acceptedResult)==AttachmentOcrApplyOutcome.Rejected,"AcceptPrepared revokes same-version result");
            foreach(Task cleanup in new[]{Task.FromException(new InvalidOperationException("synthetic cleanup failed")),Task.FromCanceled(new CancellationToken(true))})
            {
                var failed=Grant();var failedResult=Result(failed);Bind(owner,failed,new(Task.FromResult(failedResult),cleanup));
                VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,failed.Stamp.Version,failed.Stamp.PreviewEpoch,failedResult)==AttachmentOcrApplyOutcome.Rejected,"Faulted or canceled cleanup never permits apply");owner.RetireAttachmentOcrGrant(failed);Fail(()=>owner.SettleAttachmentOcrGrant(failed));Fail(()=>_=failedResult.Text);
            }
            slots.Clear();for(int i=0;i<2;i++){var g=Grant();g.Lease.Dispose();slots.Add(g);}Fail(()=>Grant());foreach(var g in slots)owner.RetireAttachmentOcrGrant(g);
            var locked=Grant();var lockedResult=Ready(locked);byte[] lockedBytes=Bytes(lockedResult);await owner.LockAsync();VaultChecks.Require(lockedBytes.All(b=>b==0),"Lock clears idle ready owned output before future apply");VaultChecks.Require(owner.ApplyAttachmentOcrResult(note,id,locked.Stamp.Version,locked.Stamp.PreviewEpoch,lockedResult)==AttachmentOcrApplyOutcome.Rejected,"Lock rejects and disposes ready result");Fail(()=>_=lockedResult.Text);
        }
        finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
        await OwnerPolicy(png);
        Console.WriteLine("Attachment OCR genuine issuer, cap, retirement, metadata-only apply and revocation checks passed.");
    }
    private static async Task OwnerPolicy(byte[] png)
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-ocr-owner-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();await owner.PrepareAttachmentsAsync();var id=owner.AttachBytes(note,png,"synthetic.png","image/png",note.EditVersion);await owner.SaveAsync();
            int thread=Environment.CurrentManagedThreadId;Func<bool> access=()=>Environment.CurrentManagedThreadId==thread;var grant=owner.CreateAttachmentOcrReadGrant(note,id,note.EditVersion,owner.AttachmentPreviewEpoch,access);
            Exception? error=null;int foreign=0;var worker=new Thread(()=>{foreign=Environment.CurrentManagedThreadId;try{owner.RetireAttachmentOcrGrant(grant);}catch(Exception ex){error=ex;}});worker.Start();worker.Join();
            VaultChecks.Require(foreign!=thread&&error is InvalidOperationException,"Foreign owner entry rejected by stable explicit policy");owner.RetireAttachmentOcrGrant(grant);
        }
        finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static byte[] Bytes(OwnedOcrDerivation result)=>(byte[])typeof(OwnedOcrDerivation).GetField("utf8",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(result)!;
    // Synthetic operation binding by private reflection is a test seam, never engine execution evidence.
    private static void Bind(SaveCoordinator owner,AttachmentOcrReadGrant grant,LinkedOcrOperation operation)
    {try{typeof(SaveCoordinator).GetMethod("BindAttachmentOcrOperation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(owner,[grant,operation]);}catch(TargetInvocationException ex)when(ex.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();}}
    private static void Fail(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected OCR authority refusal");}
}

using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

internal static class BackupMergeChecks
{
    internal static async Task Run()
    {
        // Missing API is an explicit contract RED, not a compiler-error stand-in.
        Require(typeof(SaveCoordinator).GetMethod("CreateBackupMergeView") is not null,
            "O06 genuine session/view confirmed-preview authority is missing");
        PureContracts();
        await HappyPath();
        await BackupMergeLifecycleChecks.Run();
    }
    private static object Call(object target,string name,params object[] args)
    {
        var method=target.GetType().GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length);
        try{return method.Invoke(target,args)!;}catch(TargetInvocationException e)when(e.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static async Task<object> Await(object target,string name,params object[] args)
    {
        var task=(Task)Call(target,name,args);await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    private static object Property(object target,string name)=>target.GetType().GetProperty(name)!.GetValue(target)!;
    private static void PureContracts()
    {
        var now=DateTimeOffset.UnixEpoch;var a=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"A","A");
        var ar=new StoredRevision(a.NoteId,a.RevisionId,[],now,a.Title,a.Text);
        var b=a with{RevisionId=Guid.NewGuid(),Parents=[a.RevisionId],Title="B",Text="B"};
        var c=a with{RevisionId=Guid.NewGuid(),Parents=[a.RevisionId],Title="C",Text="C"};
        var current=new VaultSnapshot(4,Guid.NewGuid(),[b]){History=[ar]};var incoming=current with{Notes=[c]};
        object Prepare(EditingWorkspace w,VaultSnapshot s)=>Call(w,"PrepareBackupMerge",s,new[]{a.NoteId},(Action<VaultSnapshot>)(_=>{}));
        void Apply(EditingWorkspace w,object p)=>Call(w,"ApplyPreparedBackupMerge",p,(Func<bool>)(()=>true),(Action)(()=>{}));
        using var workspaceScope=new Scope(new EditingWorkspace(TimeProvider.System,current));var w=workspaceScope.Workspace;
        byte[] before=SnapshotSerialization.Bytes(w.Capture());
        foreach(var variant in new[]{incoming with{History=[ar with{Title="changed identity"}]},incoming with{Notes=[c with{CreatedAt=now.AddDays(1)}]},incoming with{Notes=[c with{Metadata=new(){Deleted=true}}],Tombstones=[new(c.NoteId,c.RevisionId,c.Parents)]},incoming with{Notes=[c with{NoteId=Guid.NewGuid(),Parents=[]}],History=[]}})
        {VaultChecks.ExpectFailure(()=>Prepare(w,variant),"Unsupported selected state/immutable identity refuses entire merge");Require(before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Refusal preserves original workspace");}
        var prepared=Prepare(w,incoming);incoming.History[0].Parents.AsSpan().Clear();c.Parents[0]=Guid.NewGuid(); // Candidate owns original branch parents.
        int notifications=0;w.Changed+=()=>{notifications++;Require(w.Capture().History.Any(h=>h.RevisionId==c.RevisionId&&h.Parents.SequenceEqual(new[]{a.RevisionId})),"Deep-owned original causal parent survives caller mutation");};Apply(w,prepared);
        Require(notifications==1&&w.Capture().Notes.Single().RevisionId==b.RevisionId,"One whole history publication preserves current head");
        var descendant=b with{RevisionId=Guid.NewGuid(),Parents=[b.RevisionId],Text="descendant"};var descendantSource=current with{Notes=[descendant],History=[ar,new(b.NoteId,b.RevisionId,b.Parents,now,b.Title,b.Text)]};
        using var dw=new Scope(new EditingWorkspace(TimeProvider.System,current));Apply(dw.Workspace,Prepare(dw.Workspace,descendantSource));Require(((Guid[])Call(dw.Workspace,"PendingBackupBranchTips",dw.Workspace.Notes.Single())).SequenceEqual(new[]{descendant.RevisionId}),"Descendant branch remains pending without automatic promotion");
        using var aborted=new Scope(new EditingWorkspace(TimeProvider.System,current));var ap=Prepare(aborted.Workspace,descendantSource);Action<NoteDraft?> throwing=_=>{Require(before.SequenceEqual(SnapshotSerialization.Bytes(aborted.Workspace.Capture())),"Invalidation observes all old fields");throw new InvalidOperationException("synthetic invalidation failure");};aborted.Workspace.AttachmentReadInvalidating+=throwing;
        VaultChecks.ExpectFailure(()=>Apply(aborted.Workspace,ap),"Invalidation throw aborts before fields change");Require(before.SequenceEqual(SnapshotSerialization.Bytes(aborted.Workspace.Capture())),"Invalidation failure has no incoming history");aborted.Workspace.AttachmentReadInvalidating-=throwing;
        using var closed=new Scope(new EditingWorkspace(TimeProvider.System,current));var cp=Prepare(closed.Workspace,descendantSource);bool clearing=false;closed.Workspace.AttachmentReadInvalidating+=_=>{if(clearing)return;clearing=true;closed.Workspace.Clear();};VaultChecks.ExpectFailure(()=>Apply(closed.Workspace,cp),"Reentrant close cannot publish into a closed workspace");Require(closed.Workspace.Notes.Count==0,"Reentrant clear is preserved");
        Console.WriteLine("PASS: O06 causal union/identity/state refusal/deep ownership and invalidation-before-publication contracts");
    }
    private sealed class Scope(EditingWorkspace workspace):IDisposable
    {internal EditingWorkspace Workspace=>workspace;public void Dispose()=>workspace.Clear();}
    private static async Task HappyPath()
    {
        string directory=Path.Combine(Path.GetTempPath(),"memo-o06-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            var now=DateTimeOffset.UnixEpoch;var ancestor=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"ancestor","A");var source=new VaultSnapshot(4,Guid.NewGuid(),[ancestor]);string root=Path.Combine(directory,"vault");
            using(var seed=EncryptedVault.Create(root,secret,secret))seed.Save(source);
            using var session=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);
            var note=session.Workspace.Notes.Single();note.Title="current dirty";note.Text="B";
            var incoming=ancestor with{RevisionId=Guid.NewGuid(),Parents=[ancestor.RevisionId],Title="backup branch",Text="C"};
            var backup=source with{Notes=[incoming],History=[new(ancestor.NoteId,ancestor.RevisionId,[],now,ancestor.Title,ancestor.Text)]};
            byte[] plain=SnapshotSerialization.Bytes(backup),key=RandomNumberGenerator.GetBytes(32),cipher;
            try{cipher=VaultEnvelope.Encrypt(plain,key,secret,new(session.VaultIdentity,Guid.NewGuid(),Guid.NewGuid(),1,2,plain.Length));}
            finally{CryptographicOperations.ZeroMemory(plain);CryptographicOperations.ZeroMemory(key);}
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"source.vault");File.WriteAllBytes(path,cipher);
            using var view=(IDisposable)Call(session,"CreateBackupMergeView",(Func<bool>)(()=>true),(Func<bool>)(()=>true));
            object preview=await Await(session,"PreviewEncryptedBackupMergeAsync",cipher,new[]{note.Id},path,session.AttachmentPreviewEpoch,view);
            Require(preview.GetType().GetProperty("BranchDetails") is not null,"O06 immutable bounded actual current/incoming branch details are missing");
            var comparisons=(System.Collections.Immutable.ImmutableArray<BackupMergeBranchComparison>)Property(preview,"BranchDetails");Require(comparisons.Single().Current.TextExcerpt=="B"&&comparisons.Single().Incoming.TextExcerpt=="C"&&comparisons.Single().Incoming.RevisionId==incoming.RevisionId,"Preview compares actual clean current and selected incoming tip");
            Require(!session.IsDirty&&note.Text=="B","Dirty checkpoint completes before merge preview without incoming data");
            var clean=session.Workspace.Capture();byte[] cleanBytes=SnapshotSerialization.Bytes(clean),preCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            var token=Property(preview,"Token");Call(view,"MarkDisplayed",token);var confirmed=Call(session,"ConfirmBackupMerge",token);
            int whole=0;Action observer=()=>{whole++;var at=session.Workspace.Capture();Require(at.Notes.Single().RevisionId==clean.Notes.Single().RevisionId&&at.History.Any(h=>h.RevisionId==incoming.RevisionId),"Changed observers see original current and complete imported branch");};
            session.Workspace.Changed+=observer;
            var result=await Await(session,"MergeEncryptedBackupAsync",confirmed,cipher);
            session.Workspace.Changed-=observer;
            Require(Property(result,"Disposition").ToString()=="AppliedAndSaved"&&whole==1&&!session.IsDirty,"Same-ID branch import applies whole batch once and saves");
            var after=session.Workspace.Capture();Require(after.Notes.Length==1&&after.Notes.Single().RevisionId==clean.Notes.Single().RevisionId&&after.Notes.Single().Text=="B"&&after.History.Any(h=>h.RevisionId==incoming.RevisionId&&h.Text=="C"),"Same IDs and current content survive union without promotion");
            var tips=(Guid[])Call(session.Workspace,"PendingBackupBranchTips",note);Require(tips.SequenceEqual(new[]{incoming.RevisionId}),"Concurrent backup tip remains visibly pending");
            Require(Directory.GetFiles(root,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(preCipher)),"Exact committed premerge cipher is retained");
            Require(cipher.SequenceEqual(File.ReadAllBytes(path)),"Original encrypted backup remains exact");
            var again=await Await(session,"PreviewEncryptedBackupMergeAsync",cipher,new[]{note.Id},path,session.AttachmentPreviewEpoch,view);
            var secondToken=Property(again,"Token");Call(view,"MarkDisplayed",secondToken);var secondConfirmed=Call(session,"ConfirmBackupMerge",secondToken);
            var repeated=await Await(session,"MergeEncryptedBackupAsync",secondConfirmed,cipher);Require(Property(repeated,"Disposition").ToString()=="NoChange","Repeated same-ID branch merge is idempotent");
            bool reused=false;try{await Await(session,"MergeEncryptedBackupAsync",secondConfirmed,cipher);}catch(InvalidOperationException){reused=true;}Require(reused,"Confirmed token can only be consumed once");
            await session.LockAsync();session.Dispose();
            using var reopened=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var reopenedNote=reopened.Workspace.Notes.Single();Require(((Guid[])Call(reopened.Workspace,"PendingBackupBranchTips",reopenedNote)).SequenceEqual(new[]{incoming.RevisionId})&&reopenedNote.Text=="B"&&reopened.Workspace.DescribePendingBackupBranches(reopenedNote).Single().Incoming.TextExcerpt=="C","Pending branch/details survive real encrypted reopen");await reopened.LockAsync();
            CryptographicOperations.ZeroMemory(cipher);CryptographicOperations.ZeroMemory(cleanBytes);
            Console.WriteLine("PASS: O06 dirty checkpoint, genuine confirmed token, same-ID branch preservation/idempotency/exact recovery copy/encrypted restart");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private static void Require(bool value,string message)=>VaultChecks.Require(value,message);
}

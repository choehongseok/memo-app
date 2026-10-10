using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class BranchResolutionChecks
{
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(EditingWorkspace).GetMethod("PrepareBranchResolution",BindingFlags.Instance|BindingFlags.NonPublic) is not null,"O06 prepared two-parent keep-current resolution API is missing");
        Basic();Refusals();Reentry();Budgets();FormatsAndOcrBudgets();LegacyAndPreparation();
        await ReentrantDirtyEdit();
        Console.WriteLine("PASS: explicit maximal-tip two-parent keep-current candidates, immutable history, atomic publication/refusal and schema11 budgets");
    }
    private static object Call(object target,string name,params object[] args)
    {
        try{return target.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(target,args)!;}
        catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
    private static object Prepare(EditingWorkspace w,Guid note,Guid tip,Action<VaultSnapshot>? validate=null)=>Call(w,"PrepareBranchResolution",note,tip,validate??(_=>{}));
    private static bool Apply(EditingWorkspace w,object p,Func<bool>? current=null,Action? mark=null)=>(bool)Call(w,"ApplyPreparedBranchResolution",p,current??(()=>true),mark??(()=>{}));
    private static StoredRevision Revision(StoredNote n)=>new(n.NoteId,n.RevisionId,(Guid[])n.Parents.Clone(),n.ModifiedAt,n.Title,n.Text){Metadata=n.Metadata,Mode=n.Mode,Document=n.Document,AttachmentIds=n.AttachmentIds};
    private static (VaultSnapshot Snapshot,StoredNote Head,StoredRevision Tip,StoredRevision Other) Fixture()
    {
        var time=DateTimeOffset.UnixEpoch;var a=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],time,time,"ancestor","A");var b=a with{RevisionId=Guid.NewGuid(),Parents=[a.RevisionId],Title="current B",Text="B 한글"};var c=Revision(a with{RevisionId=Guid.NewGuid(),Parents=[a.RevisionId],Title="incoming C",Text="C"});var d=c with{RevisionId=Guid.NewGuid(),Title="other D",Text="D",ModifiedAt=time.AddDays(99)};
        var other=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],time,time,"unselected","unchanged");return(new(4,Guid.NewGuid(),[b,other]){History=[Revision(a),c,d]},b,c,d);
    }
    private static void Basic()
    {
        var (snapshot,b,c,d)=Fixture();var w=new EditingWorkspace(TimeProvider.System,snapshot);
        try
        {
            byte[] before=SnapshotSerialization.Bytes(w.Capture());var p=Prepare(w,b.NoteId,c.RevisionId);VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Prepare cannot mutate workspace");var owned=Field<VaultSnapshot>(p,"Candidate");VaultChecks.Require(owned.Notes.Single(n=>n.NoteId==b.NoteId).Parents.SequenceEqual([b.RevisionId,c.RevisionId]),"Exact ordered two parents");bool marked=false;int notified=0;w.Changed+=()=>{notified++;VaultChecks.Require(marked&&Field<bool>(p,"Applied")&&w.Capture().Notes.Single(n=>n.NoteId==b.NoteId).RevisionId==Field<Guid>(p,"NewRevisionId"),"Observers see marked whole applied state");};
            bool cleanPublication=Apply(w,p,mark:()=>marked=true);VaultChecks.Require(cleanPublication&&notified==1,$"One complete publication: clean={cleanPublication}, notifications={notified}");var after=w.Capture();var head=after.Notes.Single(n=>n.NoteId==b.NoteId);VaultChecks.Require(head.RevisionId!=b.RevisionId&&head.Title==b.Title&&head.Text==b.Text&&head.Mode==b.Mode&&head.Document==b.Document&&System.Text.Json.JsonSerializer.Serialize(head.Metadata)==System.Text.Json.JsonSerializer.Serialize(b.Metadata)&&head.AttachmentIds.SequenceEqual(b.AttachmentIds)&&head.CreatedAt==b.CreatedAt&&head.Scope==b.Scope,"Current content unchanged at fresh revision");VaultChecks.Require(System.Text.Json.JsonSerializer.Serialize(after.History.Single(r=>r.RevisionId==b.RevisionId))==System.Text.Json.JsonSerializer.Serialize(Revision(b))&&after.History.Any(r=>r.RevisionId==c.RevisionId)&&after.History.Any(r=>r.RevisionId==d.RevisionId),"Both predecessors and other branch retained");VaultChecks.Require(w.PendingBackupBranchTips(w.Notes.Single(n=>n.Id==b.NoteId)).SequenceEqual([d.RevisionId])&&Field<System.Collections.Immutable.ImmutableArray<Guid>>(p,"RemainingTips").SequenceEqual([d.RevisionId]),"Only selected tip consumed by causal ancestry");VaultChecks.Require(System.Text.Json.JsonSerializer.Serialize(after.Notes.Single(n=>n.NoteId!=b.NoteId))==System.Text.Json.JsonSerializer.Serialize(snapshot.Notes.Single(n=>n.NoteId!=b.NoteId)),"Other current note exact");VaultChecks.ExpectFailure(()=>Apply(w,p),"Prepared resolution single use");
            var restored=VaultEnvelope.ReadSnapshot(SnapshotSerialization.Bytes(after));VaultChecks.Require(restored.Notes.Single(n=>n.NoteId==b.NoteId).Parents.SequenceEqual([b.RevisionId,c.RevisionId]),"Persisted two-parent graph roundtrip");
        }
        finally{w.Clear();}
        var disconnected=new EditingWorkspace(TimeProvider.System,snapshot with{History=snapshot.History.Select(r=>r.RevisionId==c.RevisionId?r with{Parents=[]}:r).ToArray()});try{var p=Prepare(disconnected,b.NoteId,c.RevisionId);Apply(disconnected,p);VaultChecks.Require(disconnected.Capture().Notes.Single(n=>n.NoteId==b.NoteId).Parents.SequenceEqual([b.RevisionId,c.RevisionId]),"Disconnected same-note branch explicitly retained as second parent");}finally{disconnected.Clear();}
        var descendant=c with{Parents=[b.RevisionId]};var dw=new EditingWorkspace(TimeProvider.System,snapshot with{History=[snapshot.History[0],descendant]});try{var p=Prepare(dw,b.NoteId,descendant.RevisionId);Apply(dw,p);VaultChecks.Require(dw.Capture().Notes.Single(n=>n.NoteId==b.NoteId).Parents.SequenceEqual([b.RevisionId,descendant.RevisionId])&&dw.PendingBackupBranchTips(dw.Notes.Single(n=>n.Id==b.NoteId)).Length==0,"Descendant acknowledged by explicit two distinct parents");}finally{dw.Clear();}
    }
    private static void Refusals()
    {
        var (snapshot,b,c,d)=Fixture();foreach(var tip in new[]{Guid.Empty,Guid.NewGuid(),b.RevisionId,snapshot.History[0].RevisionId}){var w=new EditingWorkspace(TimeProvider.System,snapshot);try{byte[] before=SnapshotSerialization.Bytes(w.Capture());VaultChecks.ExpectFailure(()=>Prepare(w,b.NoteId,tip),"Missing/current/ancestor tip refuses");VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Refusal whole workspace unchanged");}finally{w.Clear();}}
        var child=c with{RevisionId=Guid.NewGuid(),Parents=[c.RevisionId]};foreach(var source in new[]{snapshot with{History=snapshot.History.Append(child).ToArray()},snapshot with{History=snapshot.History.Select(r=>r.RevisionId==c.RevisionId?r with{Metadata=r.Metadata with{Deleted=true}}:r).ToArray()},snapshot with{Notes=snapshot.Notes.Select(n=>n.NoteId==b.NoteId?n with{Metadata=n.Metadata with{Deleted=true}}:n).ToArray(),Tombstones=[new(b.NoteId,b.RevisionId,b.Parents)]}}){var w=new EditingWorkspace(TimeProvider.System,source);try{VaultChecks.ExpectFailure(()=>Prepare(w,b.NoteId,c.RevisionId),"Nonmaximal/deleted source/current trash refuses");}finally{w.Clear();}}
        var scoped=new EditingWorkspace(TimeProvider.System,snapshot with{Notes=snapshot.Notes.Select(n=>n.NoteId==b.NoteId?n with{Scope="synced"}:n).ToArray()});try{VaultChecks.ExpectFailure(()=>Prepare(scoped,b.NoteId,c.RevisionId),"Synced scope unsupported by current offline resolution");}finally{scoped.Clear();}
        var wrong=new EditingWorkspace(TimeProvider.System,snapshot);try{VaultChecks.ExpectFailure(()=>Prepare(wrong,snapshot.Notes[1].NoteId,c.RevisionId),"Foreign note tip refuses");wrong.Notes[0].Text="dirty";VaultChecks.ExpectFailure(()=>Prepare(wrong,b.NoteId,c.RevisionId),"Dirty provisional basis refuses");}finally{wrong.Clear();}
        var stale=new EditingWorkspace(TimeProvider.System,snapshot);try{var p=Prepare(stale,b.NoteId,c.RevisionId);stale.Notes[1].Title="later edit";VaultChecks.ExpectFailure(()=>Apply(stale,p),"Unselected note change revokes whole candidate");VaultChecks.Require(!Field<bool>(p,"Applied"),"Stale prepared never applied");}finally{stale.Clear();}
    }
    private static void Reentry()
    {
        var (snapshot,b,c,d)=Fixture();foreach(int action in Enumerable.Range(0,6))
        {
            var w=new EditingWorkspace(TimeProvider.System,snapshot);try{var note=w.Notes.Single(n=>n.Id==b.NoteId);var p=Prepare(w,b.NoteId,c.RevisionId);bool called=false;bool Current(){if(!called){called=true;if(action==0)note.Title="predicate edit";else if(action==1)w.AcceptPrepared(snapshot);else if(action==2)w.CreateNote();else if(action==3)w.Clear();else if(action==4)throw new InvalidOperationException("predicate error");else w.Notes.Single(n=>n.Id!=b.NoteId).Favorite=true;}return true;}VaultChecks.ExpectFailure(()=>Apply(w,p,Current),"Predicate reentry rejects before fields");VaultChecks.Require(!Field<bool>(p,"Applied")&&(w.Notes.Count==0||w.Capture().Notes.Single(n=>n.NoteId==b.NoteId).RevisionId!=Field<Guid>(p,"NewRevisionId")),"Reentrant original action cannot publish prepared candidate");}finally{w.Clear();}
        }
        foreach(int action in Enumerable.Range(0,4))
        {
            var w=new EditingWorkspace(TimeProvider.System,snapshot);try{var p=Prepare(w,b.NoteId,c.RevisionId);bool once=false,observedOld=false,observedSource=false;w.AttachmentReadInvalidating+=source=>{if(once)return;once=true;observedSource=source?.Id==b.NoteId;observedOld=!Field<bool>(p,"Applied")&&w.Capture().Notes.Single(n=>n.NoteId==b.NoteId).RevisionId==b.RevisionId;if(action==0)throw new InvalidOperationException("invalidation error");else if(action==1)w.Notes[0].Title="invalidation edit";else if(action==2)w.Clear();else Apply(w,p);};VaultChecks.ExpectFailure(()=>Apply(w,p),"Invalidation throw/edit/clear/nested apply refuses");VaultChecks.Require(once&&observedOld&&observedSource,"Invalidation handler actually ran and independently observed original target fields");VaultChecks.Require(!Field<bool>(p,"Applied"),"Failed invalidation never stages fields");}finally{w.Clear();}
        }
        foreach(bool markerThrows in new[]{false,true}){var w=new EditingWorkspace(TimeProvider.System,snapshot);try{var p=Prepare(w,b.NoteId,c.RevisionId);if(!markerThrows)w.Changed+=()=>throw new InvalidOperationException("observer throw");bool clean=Apply(w,p,mark:markerThrows?()=>throw new InvalidOperationException("marker throw"):(()=>{}));VaultChecks.Require(!clean&&Field<bool>(p,"Applied")&&w.Capture().Notes.Single(n=>n.NoteId==b.NoteId).Parents.SequenceEqual([b.RevisionId,c.RevisionId]),"Throw after complete swap reports observer failure with inert applied marker true");}finally{w.Clear();}}
    }
    private static async Task ReentrantDirtyEdit()
    {
        var (snapshot,b,c,d)=Fixture();string root=Path.Combine(Path.GetTempPath(),"memo-o06-real-reentrant-edit-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using(var seed=EncryptedVault.Create(root,secret,secret))seed.Save(snapshot);
            using var session=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System);var w=session.Workspace;var note=w.Notes.Single(n=>n.Id==b.NoteId);var p=Prepare(w,b.NoteId,c.RevisionId);int changed=0;bool edited=false;w.Changed+=()=>changed++;
            note.PropertyChanged+=(_,e)=>{if(!edited&&e.PropertyName==nameof(NoteDraft.EditVersion)&&ReferenceEquals(e,note.PreparedRevisionNotification)){edited=true;note.Text="real reentrant edit 한글";}};
            VaultChecks.Require(Apply(w,p)&&edited&&changed==2&&session.IsDirty,"Exactly one prepared Changed plus real reentrant EditVersion still marks dirty");var dirty=w.Capture();Guid merged=Field<Guid>(p,"NewRevisionId");var current=dirty.Notes.Single(n=>n.NoteId==b.NoteId);VaultChecks.Require(current.Text=="real reentrant edit 한글"&&current.RevisionId!=merged&&current.Parents.SequenceEqual([merged])&&dirty.History.Single(r=>r.RevisionId==merged).Text==b.Text&&dirty.History.Single(r=>r.RevisionId==merged).Parents.SequenceEqual([b.RevisionId,c.RevisionId]),"Real observer edit creates a child of complete two-parent resolution without losing predecessor");VaultChecks.Require(await session.SaveAsync()&&!session.IsDirty,"Actual encrypted save retains real reentrant edit");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void LegacyAndPreparation()
    {
        foreach(var fixture in new[]{(Schema:8,Sha:"96845312b47eca214aaf8a72fa5a41c650f18dc3be0c516791257375daf5925f"),(Schema:9,Sha:"cde32f87ced6cbf5828c2c6ad3547e23243588aa719d727cd963fc6945518050"),(Schema:10,Sha:"da2e67991474b41fc02315b44f63125305a172bf1a19ebf6b66e2e244ec9d28e")})
        {
            byte[] bytes=File.ReadAllBytes($"tests/fixtures/storage/frozen-schema{fixture.Schema}.json");VaultChecks.Require(Convert.ToHexStringLower(SHA256.HashData(bytes))==fixture.Sha,"Frozen previous writer fixture exact hash");var old=VaultEnvelope.ReadSnapshot(bytes);VaultChecks.Require(SnapshotSerialization.Bytes(old).SequenceEqual(bytes),"Legacy8/9/10 writer remains exact");var head=old.Notes.First(n=>!n.Metadata.Deleted);var tip=Revision(head) with{RevisionId=Guid.NewGuid(),Parents=[head.RevisionId],Title="retained descendant"};foreach(int schema in new[]{fixture.Schema,11}){var source=old with{SchemaVersion=schema,History=old.History.Append(tip).ToArray()};var w=new EditingWorkspace(TimeProvider.System,source);try{var p=Prepare(w,head.NoteId,tip.RevisionId);Apply(w,p);VaultChecks.Require(w.Capture().SchemaVersion==schema&&w.Capture().Notes.Single(n=>n.NoteId==head.NoteId).Parents.SequenceEqual([head.RevisionId,tip.RevisionId]),"Resolution preserves known schema without schema extension or downgrade");}finally{w.Clear();}}
        }
        var (snapshot,b,c,d)=Fixture();foreach(bool mutate in new[]{false,true}){var w=new EditingWorkspace(TimeProvider.System,snapshot);try{byte[] before=SnapshotSerialization.Bytes(w.Capture());VaultChecks.ExpectFailure(()=>Prepare(w,b.NoteId,c.RevisionId,_=>{if(mutate)w.Notes[1].Favorite=true;else throw new InvalidOperationException("validation refused");}),"Validation callback throw/reentry refuses prepared authority");if(!mutate)VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Validation refusal original workspace exact");}finally{w.Clear();}}
        var owner=new EditingWorkspace(TimeProvider.System,snapshot);var other=new EditingWorkspace(TimeProvider.System,snapshot);try{var p=Prepare(owner,b.NoteId,c.RevisionId);VaultChecks.ExpectFailure(()=>Apply(other,p),"Same IDs/basis other workspace cannot use owner-prepared state");VaultChecks.ExpectFailure(()=>Apply(owner,p),"Failed owner substitution consumes original candidate");VaultChecks.Require(!Field<bool>(p,"Applied"),"Rejected owner replay never applies");}finally{owner.Clear();other.Clear();}
        var deep=new EditingWorkspace(TimeProvider.System,snapshot);try{var p=Prepare(deep,b.NoteId,c.RevisionId);var candidate=Field<VaultSnapshot>(p,"Candidate");byte[] detached=SnapshotSerialization.Bytes(candidate);c.Parents[0]=Guid.NewGuid();VaultChecks.Require(detached.SequenceEqual(SnapshotSerialization.Bytes(candidate)),"Prepared candidate owns mutable predecessor arrays");VaultChecks.ExpectFailure(()=>Apply(deep,p),"Changed caller-owned original basis revokes prepared identity");}finally{deep.Clear();}
    }
    private static void FormatsAndOcrBudgets()
    {
        var (snapshot,b,c,d)=Fixture();var opaque=new MemoApp.Core.Documents.StyledDocument(1,"{ \"nodes\" : [{\"type\":\"paragraph\",\"runs\":[{\"text\":\"opaque\"}],\"future\":true}] }");
        foreach(var original in new[]{b with{Mode="markdown",Text="# original\r\n**exact**"},b with{Mode="rich",Document=opaque,Text="inert opaque projection"}})
        {
            var source=snapshot with{Notes=snapshot.Notes.Select(n=>n.NoteId==b.NoteId?original:n).ToArray()};var w=new EditingWorkspace(TimeProvider.System,source);try{var p=Prepare(w,b.NoteId,c.RevisionId);Apply(w,p);var head=w.Capture().Notes.Single(n=>n.NoteId==b.NoteId);VaultChecks.Require(head.Mode==original.Mode&&head.Text==original.Text&&head.Document?.SourceJson==original.Document?.SourceJson&&w.Capture().History.Single(r=>r.RevisionId==original.RevisionId).Document?.SourceJson==original.Document?.SourceJson,"Markdown and opaque v1 raw source preserved in new head and predecessor");}finally{w.Clear();}
        }
        string root=Path.Combine(Path.GetTempPath(),"memo-o06-resolution-model-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[] png=Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64"));
        try
        {
            using var vault=EncryptedVault.Create(root,secret,secret);var rooted=vault.InitializeAttachmentRoot(vault.Loaded);vault.Save(rooted);var obj=vault.EncryptAttachment(png,"SYNTHETIC.png","image/png");
            var facts=new StoredOcrProvenance("db0ec62f81b0737fbbe184d8fea40af5738f8eef","13275a278eb55b5746e33f95fbf5a2c8f604b3ab","87416418657359cb625c412a48b6e1d6d41c29bd",new string('a',64),"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2","7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2","kor+eng",1,6,"png-nearest-1024-straight-alpha-white-ppm-v1",1,1,1,1,new string('b',64));
            StoredAttachmentOcrResult Ocr(string text)=>new(obj.ObjectId,obj.Sha256,text,Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))),facts);
            Guid profile=Guid.NewGuid();var rich=MemoApp.Core.Documents.RichDocumentImageEdit.Insert(MemoApp.Core.Documents.RichDocumentCodec.FromPlain(b.Text),1,obj.ObjectId,"SYNTHETIC.png");var original=b with{Mode="rich",Document=rich,Text=MemoApp.Core.Documents.RichDocumentCodec.Inspect(rich).Text!,AttachmentIds=[obj.ObjectId],Metadata=b.Metadata with{AttachmentOcrResults=[Ocr("historical OCR 한글")],FilePathLinks=[new(Guid.NewGuid(),profile,"SOURCE.txt",@"C:\SYNTHETIC\SOURCE.txt")]}};
            var source=snapshot with{SchemaVersion=11,AttachmentRootId=rooted.AttachmentRootId,AttachmentObjects=[obj],UiDevices=[new(profile,new(),[])],Notes=snapshot.Notes.Select(n=>n.NoteId==b.NoteId?original:n).ToArray()};VaultEnvelope.Validate(source);
            var w=new EditingWorkspace(TimeProvider.System,source);try{var p=Prepare(w,b.NoteId,c.RevisionId,vault.ValidateImportedCandidate);Apply(w,p);var after=w.Capture();var head=after.Notes.Single(n=>n.NoteId==b.NoteId);VaultChecks.Require(head.Document==rich&&head.Text==original.Text&&head.AttachmentIds.SequenceEqual(original.AttachmentIds)&&System.Text.Json.JsonSerializer.Serialize(head.Metadata)==System.Text.Json.JsonSerializer.Serialize(original.Metadata)&&after.AttachmentObjects.SequenceEqual(source.AttachmentObjects)&&System.Text.Json.JsonSerializer.Serialize(after.UiDevices)==System.Text.Json.JsonSerializer.Serialize(source.UiDevices),"Current v2 image, immutable cipher object, OCR/file links and device settings preserved");byte[] decoded=vault.DecryptAttachment(after.AttachmentObjects.Single());try{VaultChecks.Require(decoded.SequenceEqual(png),"Actual current-root authentication preserves exact PNG original bytes");}finally{CryptographicOperations.ZeroMemory(decoded);}}finally{w.Clear();}
            var ocrHistory=Enumerable.Range(0,127).Select(_=>Revision(original with{RevisionId=Guid.NewGuid(),Parents=[]})).ToArray();var crowded=source with{History=source.History.Concat(ocrHistory).ToArray()};VaultEnvelope.Validate(crowded);RejectBudget(crowded,b.NoteId,c.RevisionId,"occurrence",false);
            string largeText=new string('한',65536);var large=Ocr(largeText);var largeHead=original with{Metadata=original.Metadata with{AttachmentOcrResults=[large]}};var largeHistory=Enumerable.Range(0,4).Select(_=>Revision(largeHead with{RevisionId=Guid.NewGuid(),Parents=[]})).ToArray();var aggregate=source with{Notes=source.Notes.Select(n=>n.NoteId==b.NoteId?largeHead:n).ToArray(),History=source.History.Concat(largeHistory).ToArray()};VaultEnvelope.Validate(aggregate);RejectBudget(aggregate,b.NoteId,c.RevisionId,"aggregate",false);
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(png);if(Directory.Exists(root))Directory.Delete(root,true);}
        string escaped=new string('\u0001',65536);var escapedHead=b with{Text=escaped};var fill=snapshot with{Notes=snapshot.Notes.Select(n=>n.NoteId==b.NoteId?escapedHead:n).Concat(Enumerable.Range(0,41).Select(_=>escapedHead with{NoteId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Parents=[]})).ToArray()};VaultEnvelope.Validate(fill);RejectBudget(fill,b.NoteId,c.RevisionId,"escaped16MiB",true);
    }
    private static void RejectBudget(VaultSnapshot source,Guid noteId,Guid tip,string reason,bool payload)
    {
        var w=new EditingWorkspace(TimeProvider.System,source);try{byte[] before=SnapshotSerialization.Bytes(w.Capture());bool refused=false;try{Prepare(w,noteId,tip);}catch(InvalidDataException e)when(!payload||e.Message.Contains("payload byte budget",StringComparison.Ordinal)){refused=true;}VaultChecks.Require(refused&&before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Old-current history duplication refuses "+reason+" budget atomically");}finally{w.Clear();}
    }
    private static void Budgets()
    {
        var (snapshot,b,c,d)=Fixture();foreach(int historyCount in new[]{511,512})
        {
            var history=snapshot.History.Concat(Enumerable.Range(0,historyCount-snapshot.History.Length).Select(_=>c with{RevisionId=Guid.NewGuid(),Parents=[]})).ToArray();var w=new EditingWorkspace(TimeProvider.System,snapshot with{History=history});try{if(historyCount==511){var p=Prepare(w,b.NoteId,c.RevisionId);Apply(w,p);VaultChecks.Require(w.Capture().History.Count(r=>r.NoteId==b.NoteId)==512,"511 to512 history exact boundary");}else{var before=SnapshotSerialization.Bytes(w.Capture());VaultChecks.ExpectFailure(()=>Prepare(w,b.NoteId,c.RevisionId),"512 to513 refuses without pruning");VaultChecks.Require(before.SequenceEqual(SnapshotSerialization.Bytes(w.Capture())),"Budget refusal unchanged");}}finally{w.Clear();}
        }
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Storage;
using MemoApp.Core.Editing;
internal static class AttachmentMutationChecks
{
    private const BindingFlags Access=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
    private delegate Guid Attach(NoteDraft note,ReadOnlySpan<byte> bytes,string name,string mime,long version);
    private static T Bind<T>(object owner,string method)where T:Delegate=>owner.GetType().GetMethod(method,Access)!.CreateDelegate<T>(owner);
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(EditingWorkspace).GetMethod("AddAttachment",Access) is not null&&typeof(SaveCoordinator).GetMethod("AttachBytes",Access) is not null,"Atomic attachment add/detach with session source authority is missing");
        var root=Guid.NewGuid();var vaultId=Guid.NewGuid();var key=RandomNumberGenerator.GetBytes(32);var body=RandomNumberGenerator.GetBytes(1234);var now=DateTimeOffset.UtcNow;
        try
        {
            var item=AttachmentObjectCodec.Encrypt(body,vaultId,root,key,"合成 original.pdf","application/pdf");
            var basis=new VaultSnapshot(5,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"one","body"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"other","unchanged")]){AttachmentRootId=root};
            var workspace=new EditingWorkspace(TimeProvider.System,basis);var note=workspace.Notes[0];var other=workspace.Notes[1];var add=Bind<Action<NoteDraft,StoredAttachmentObject>>(workspace,"AddAttachment");var detach=Bind<Action<NoteDraft,Guid>>(workspace,"DetachAttachment");
            long content=note.ContentVersion;add(note,item);var attached=workspace.Capture();
            VaultChecks.Require(attached.AttachmentObjects.Single()==item&&note.AttachmentIds.SequenceEqual([item.ObjectId])&&note.ContentVersion>content&&attached.History.Single().AttachmentIds.Length==0&&attached.Notes.Single(n=>n.NoteId==other.Id)==basis.Notes[1],"Add publishes one complete immutable object+refs/new revision atomically while preserving other note");
            string before=JsonSerializer.Serialize(workspace.Capture()),frozen=JsonSerializer.Serialize(workspace.FrozenBasis);long version=note.EditVersion;int events=0;workspace.Changed+=()=>events++;
            VaultChecks.ExpectFailure(()=>add(note,item),"Existing object ID is immutable, not reusable or duplicable");VaultChecks.ExpectFailure(()=>add(note,item with{ObjectId=Guid.NewGuid(),RootId=Guid.NewGuid()}),"Wrong-root whole operation refusal");
            VaultChecks.Require(events==0&&note.EditVersion==version&&JsonSerializer.Serialize(workspace.Capture())==before&&JsonSerializer.Serialize(workspace.FrozenBasis)==frozen,"Rejected add leaves complete basis/draft/version/events unchanged");
            var copy=workspace.Duplicate(note);detach(note,item.ObjectId);var detached=workspace.Capture();
            VaultChecks.Require(note.AttachmentIds.Length==0&&copy.AttachmentIds.SequenceEqual([item.ObjectId])&&detached.AttachmentObjects.Single()==item&&detached.History.Any(r=>r.NoteId==note.Id&&r.AttachmentIds.Contains(item.ObjectId)),"Detach only removes active ref in a new revision, keeps full object and duplicate/history references");
            var past=workspace.HistoryFor(note).First(r=>r.AttachmentIds.Contains(item.ObjectId));workspace.RestoreRevision(note,past.RevisionId);VaultChecks.Require(note.AttachmentIds.SequenceEqual([item.ObjectId]),"Restoring attachment revision restores original immutable ID");
            Limits(basis,item,key,vaultId,root,now);workspace.Clear();VaultChecks.Require(note.AttachmentIds.Length==0,"Close immediately drops attachment draft refs");
            await RealSession(body);
            await WriteFailure(body);
            Console.WriteLine("PASS: atomic attachment add/detach/new revisions, unchanged rejection at history/ref/object/aggregate/payload limits, session/selection/source/lock authority and real encrypted restart/backup (opaque-file UI not connected)");
        }
        finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(body);}
    }
    private static void Limits(VaultSnapshot basis,StoredAttachmentObject item,byte[] key,Guid vaultId,Guid root,DateTimeOffset now)
    {
        void Refuse(VaultSnapshot initial,Action<EditingWorkspace,NoteDraft> action,string reason)
        {
            VaultEnvelope.Validate(initial); // Structural-limit fixtures must start valid, not fail for an already-invalid baseline.
            var workspace=new EditingWorkspace(TimeProvider.System,initial);var note=workspace.Notes[0];string before=JsonSerializer.Serialize(workspace.Capture()),frozen=JsonSerializer.Serialize(workspace.FrozenBasis);long version=note.EditVersion,content=note.ContentVersion;int events=0;workspace.Changed+=()=>events++;
            VaultChecks.ExpectFailure(()=>action(workspace,note),reason);VaultChecks.Require(events==0&&note.EditVersion==version&&note.ContentVersion==content&&JsonSerializer.Serialize(workspace.Capture())==before&&JsonSerializer.Serialize(workspace.FrozenBasis)==frozen,"Whole-limit refusal leaves complete model/basis/events/version unchanged");workspace.Clear();
        }
        var fullHistory=basis with{History=Enumerable.Range(0,512).Select(_=>new StoredRevision(basis.Notes[0].NoteId,Guid.NewGuid(),[],now,"old","old body")).ToArray()};
        Refuse(fullHistory,(w,n)=>Bind<Action<NoteDraft,StoredAttachmentObject>>(w,"AddAttachment")(n,item),"Add history512 cap");
        var withRef=fullHistory with{Notes=[fullHistory.Notes[0] with{AttachmentIds=[item.ObjectId]},fullHistory.Notes[1]],AttachmentObjects=[item]};
        Refuse(withRef,(w,n)=>Bind<Action<NoteDraft,Guid>>(w,"DetachAttachment")(n,item.ObjectId),"Detach history512 cap");
        var objects=Enumerable.Range(0,16).Select(i=>item with{ObjectId=Guid.NewGuid(),Name="synthetic"+i+".pdf"}).ToArray();var fullRefs=basis with{AttachmentObjects=[..objects],Notes=[basis.Notes[0] with{AttachmentIds=[..objects.Select(x=>x.ObjectId)]},basis.Notes[1]]};
        Refuse(fullRefs,(w,n)=>Bind<Action<NoteDraft,StoredAttachmentObject>>(w,"AddAttachment")(n,item),"Per-note16 references cap");
        var many=Enumerable.Range(0,128).Select(i=>item with{ObjectId=Guid.NewGuid(),Name="object"+i+".pdf"}).ToArray();Refuse(basis with{AttachmentObjects=[..many]},(w,n)=>Bind<Action<NoteDraft,StoredAttachmentObject>>(w,"AddAttachment")(n,item),"All retained objects128 cap even if unreferenced");
        var big=AttachmentObjectCodec.Encrypt(new byte[4194304],vaultId,root,key,"large synthetic.bin","application/octet-stream");
        Refuse(basis with{AttachmentObjects=[big,big with{ObjectId=Guid.NewGuid()}]},(w,n)=>Bind<Action<NoteDraft,StoredAttachmentObject>>(w,"AddAttachment")(n,item),"All retained original lengths8MiB cap");
        var near=basis with{History=Enumerable.Range(0,250).Select(_=>new StoredRevision(basis.Notes[0].NoteId,Guid.NewGuid(),[],now,"old",new string('x',65536))).ToArray()};
        Refuse(near,(w,n)=>Bind<Action<NoteDraft,StoredAttachmentObject>>(w,"AddAttachment")(n,big),"Full serialized16MiB budget including cipher base64/history");
    }
    private static async Task RealSession(byte[] body)
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-attach-mutate-"+Guid.NewGuid().ToString("N"));var backup=root+"-backup.vault";var secret=RandomNumberGenerator.GetBytes(32);Guid noteId=Guid.Empty,objectId=Guid.Empty;
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Title="synthetic attachment note";noteId=note.Id;var attach=Bind<Attach>(session,"AttachBytes");
                VaultChecks.ExpectFailure(()=>attach(note,body,"before.bin","application/octet-stream",note.EditVersion),"No model publication before root anchor");
                VaultChecks.Require(await session.EnsureAttachmentRootAsync(),"Durably anchor root before reading/encrypting file bytes");
                long stale=note.EditVersion;note.Title="newer title";VaultChecks.ExpectFailure(()=>attach(note,body,"stale.pdf","application/pdf",stale),"Stale selected-note source version rejected before encryption/model mutation");
                var foreign=new EditingWorkspace(TimeProvider.System).CreateNote();VaultChecks.ExpectFailure(()=>attach(foreign,body,"foreign.pdf","application/pdf",foreign.EditVersion),"Foreign note membership rejected");
                objectId=attach(note,body,"합성 preserved.pdf","application/pdf",note.EditVersion);VaultChecks.Require(session.IsDirty&&note.AttachmentIds.Contains(objectId),"Attach reports a dirty in-memory change until committed");VaultChecks.Require(await session.SaveAsync(),"Real encrypted object+note atomic save");
                var read=Bind<Func<NoteDraft,Guid,long,byte[]>>(session,"ReadAttachmentBytes");var plaintext=read(note,objectId,note.EditVersion);try{VaultChecks.Require(plaintext.SequenceEqual(body),"Authorized exact active-note original bytes");}finally{CryptographicOperations.ZeroMemory(plaintext);}
                var other=session.Workspace.CreateNote();VaultChecks.ExpectFailure(()=>read(other,objectId,other.EditVersion),"Cannot read a reference owned by a different note");
                VaultChecks.Require(await session.BackupAsync(backup),"Appended attachment full committed backup succeeds");
                var copy=Path.Combine(root,"backup-copy");var candidate=EncryptedVault.ImportEncryptedCopy(copy,backup,secret);
                using(var recovered=EncryptedVault.Open(copy,secret,candidate))
                {recovered.Save(recovered.Loaded);using var restored=new SaveCoordinator(recovered,TimeProvider.System);var restoredNote=restored.Workspace.Notes.Single(n=>n.Id==noteId);var bytes=Bind<Func<NoteDraft,Guid,long,byte[]>>(restored,"ReadAttachmentBytes")(restoredNote,objectId,restoredNote.EditVersion);try{VaultChecks.Require(bytes.SequenceEqual(body),"Real appended file restores from exact one-file backup in independent directory");}finally{CryptographicOperations.ZeroMemory(bytes);}await restored.LockAsync();}
                await session.LockAsync();VaultChecks.ExpectFailure(()=>attach(note,body,"late.pdf","application/pdf",note.EditVersion),"Immediate lock revokes add");VaultChecks.ExpectFailure(()=>read(note,objectId,note.EditVersion),"Immediate lock revokes plaintext read");
            }
            using(var session=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System))
            {var note=session.Workspace.Notes.Single(n=>n.Id==noteId);VaultChecks.Require(note.AttachmentIds.Contains(objectId),"Real recovery-only restart retains appended file ref");var bytes=Bind<Func<NoteDraft,Guid,long,byte[]>>(session,"ReadAttachmentBytes")(note,objectId,note.EditVersion);try{VaultChecks.Require(bytes.SequenceEqual(body),"Real appended object decrypt after fresh key owner restart");}finally{CryptographicOperations.ZeroMemory(bytes);}Bind<Action<NoteDraft,Guid,long>>(session,"DetachAttachment")(note,objectId,note.EditVersion);VaultChecks.Require(note.AttachmentIds.Length==0&&session.Workspace.Capture().AttachmentObjects.Any(o=>o.ObjectId==objectId)&&await session.SaveAsync(),"Real detach retains encrypted bytes for history/backup");await session.LockAsync();}
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);if(File.Exists(backup))File.Delete(backup);}
    }
    private static async Task WriteFailure(byte[] body)
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-attach-fault-"+Guid.NewGuid().ToString("N"));var pending=root+"-recovery.vault";var secret=RandomNumberGenerator.GetBytes(32);var files=new AfterAnchorFault();
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret,files))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Title="synthetic fault";VaultChecks.Require(await session.EnsureAttachmentRootAsync(),"First durable root succeeds before selected file publication");var original=File.ReadAllBytes(Path.Combine(root,"current.vault"));
                var id=Bind<Attach>(session,"AttachBytes")(note,body,"retained-unsaved.pdf","application/pdf",note.EditVersion);files.Fail=true;
                VaultChecks.Require(!await session.SaveAsync()&&session.IsDirty&&note.AttachmentIds.Contains(id)&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(original),"Save flush fault truthfully keeps unsaved object/ref state and exact old current ciphertext");
                await session.LockAsync();VaultChecks.Require(session.KeysReleased&&session.PendingKind=="ciphertext","Failed appended save retains self-contained encrypted recovery and releases keys");session.ExportPendingCiphertext(pending);
                var copy=Path.Combine(root,"recover-copy");var candidate=EncryptedVault.ImportEncryptedCopy(copy,pending,secret);using var recovered=EncryptedVault.Open(copy,secret,candidate);recovered.Save(recovered.Loaded);using var restored=new SaveCoordinator(recovered,TimeProvider.System);var restoredNote=restored.Workspace.Notes.Single();var bytes=Bind<Func<NoteDraft,Guid,long,byte[]>>(restored,"ReadAttachmentBytes")(restoredNote,id,restoredNote.EditVersion);try{VaultChecks.Require(bytes.SequenceEqual(body),"Appended failed-save pending ciphertext genuinely restores every original file byte");}finally{CryptographicOperations.ZeroMemory(bytes);}await restored.LockAsync();
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);if(File.Exists(pending))File.Delete(pending);}
    }
    private sealed class AfterAnchorFault:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool Fail;
        public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic attachment flush fault");actual.FlushToDisk(stream);}public void Replace(string temp,string current,string previous)=>actual.Replace(temp,current,previous);public void Move(string temp,string current)=>actual.Move(temp,current);
    }
}

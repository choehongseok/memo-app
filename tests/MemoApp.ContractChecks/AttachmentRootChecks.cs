using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class AttachmentRootChecks
{
    private delegate StoredAttachmentObject Seal(ReadOnlySpan<byte> bytes,string name,string mime);
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static bool Anchored(EncryptedVault vault)=>(bool)typeof(EncryptedVault).GetProperty("AttachmentRootAnchored",Private)!.GetValue(vault)!;
    private static T Bind<T>(EncryptedVault vault,string name)where T:Delegate=>typeof(EncryptedVault).GetMethod(name,Private)!.CreateDelegate<T>(vault);
    private static byte[] OwnedRoot(EncryptedVault vault)=>(byte[])typeof(EncryptedVault).GetField("attachmentRootKey",Private)!.GetValue(vault)!;
    internal static async Task Run()
    {
        VaultChecks.Require(typeof(EncryptedVault).GetMethod("InitializeAttachmentRoot",Private) is not null,"Vault-owned durable attachment root and3-wrap reservations are missing");
        var path=Path.Combine(Path.GetTempPath(),"memo-root-check-"+Guid.NewGuid().ToString("N"));var secret=RandomNumberGenerator.GetBytes(32);var original=RandomNumberGenerator.GetBytes(70001);
        try
        {
            var now=DateTimeOffset.UtcNow;var noteId=Guid.NewGuid();var historyId=Guid.NewGuid();var folder=Guid.NewGuid();
            var legacy=new VaultSnapshot(4,Guid.NewGuid(),[new(noteId,Guid.NewGuid(),[historyId],now,now,"합성 첨부","plain original"){Metadata=new(){FolderId=folder}}]){Folders=[new(folder,null,"合成 root folder")],History=[new(noteId,historyId,[],now,"past","past plain")]};
            using(var initial=EncryptedVault.Create(path,secret,secret))initial.Save(legacy);
            var oldBytes=File.ReadAllBytes(Path.Combine(path,"current.vault"));VaultSnapshot stored;
            using(var vault=EncryptedVault.Open(path,secret))
            {
                var init=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot");var seal=Bind<Seal>(vault,"EncryptAttachment");var open=Bind<Func<StoredAttachmentObject,byte[]>>(vault,"DecryptAttachment");
                VaultChecks.Require(!Anchored(vault),"Legacy vault has no durable attachment root");VaultChecks.ExpectFailure(()=>seal(original,"합성.bin","application/octet-stream"),"No object encryption before root initialization/anchor");
                var next=init(legacy);var root=OwnedRoot(vault);VaultChecks.Require(next.SchemaVersion==5&&next.AttachmentRootId!=Guid.Empty&&next.AttachmentObjects.Length==0&&!Anchored(vault),"Initializing a root is not anchoring it");
                VaultChecks.Require(init(legacy).AttachmentRootId==next.AttachmentRootId,"Root initialization retries keep one owner identity");VaultChecks.Require(File.ReadAllBytes(Path.Combine(path,"current.vault")).SequenceEqual(oldBytes),"Root initialization alone never touches old current bytes");
                var prepared=vault.Prepare(next);VaultChecks.Require(!Anchored(vault),"Prepare is not a durable root anchor");VaultChecks.ExpectFailure(()=>seal(original,"합성.bin","application/octet-stream"),"No object encryption after prepare but before actual current commit");
                vault.Commit(prepared);VaultChecks.Require(Anchored(vault),"Only exact authenticated current fingerprint commit anchors root");
                VaultChecks.Require(Directory.GetFiles(path,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(oldBytes)),"First envelope2 migration retains exact original legacy ciphertext");
                var item=seal(original,"합성.bin","application/octet-stream");var pastItem=seal([1,2,3],"old.bin","application/octet-stream");
                stored=next with{AttachmentObjects=[item,pastItem],Notes=[next.Notes.Single() with{AttachmentIds=[item.ObjectId]}],History=[next.History.Single() with{AttachmentIds=[pastItem.ObjectId]}]};
                vault.Save(stored);var restored=open(item);try{VaultChecks.Require(restored.SequenceEqual(original),"Vault-owned anchored root decrypts original bytes");}finally{CryptographicOperations.ZeroMemory(restored);}
                VaultChecks.ExpectFailure(()=>vault.Prepare(stored with{AttachmentObjects=[item with{Name="changed historical name.bin"},pastItem]}),"An existing immutable object cannot change metadata under the same ID for all historical references");
                VaultChecks.ExpectFailure(()=>vault.Prepare(stored with{AttachmentObjects=[item],History=[stored.History.Single() with{AttachmentIds=[]}]}),"Existing retained objects cannot silently disappear from a normal snapshot even if newly unreferenced");
                VaultChecks.ExpectFailure(()=>vault.Prepare(legacy),"No ordinary downgrade once root is initialized");VaultChecks.ExpectFailure(()=>vault.Prepare(stored with{AttachmentRootId=Guid.NewGuid()}),"No mixed/rotated root identity");
                foreach(var file in Directory.GetFiles(path,"*.vault")){var bytes=File.ReadAllBytes(file);VaultChecks.Require(bytes.AsSpan().IndexOf(original.AsSpan(0,32))<0&&bytes.AsSpan().IndexOf(root)<0,"No original/root key bytes in current/previous/pending");}
                vault.ReleaseKeys();VaultChecks.Require(root.All(b=>b==0)&&!Anchored(vault),"ReleaseKeys zeroes private root and ends active anchor authority");VaultChecks.ExpectFailure(()=>open(item),"No decryption after key release");VaultChecks.ExpectFailure(()=>seal(original,"locked.bin","application/octet-stream"),"No encryption after key release");
            }
            using(var reopened=EncryptedVault.Open(path,secret))
            {
                VaultChecks.Require(Anchored(reopened)&&JsonSerializer.Serialize(reopened.Loaded)==JsonSerializer.Serialize(stored),"Recovery-only restart restores schema5 full objects/history and durable root");
                var plaintext=Bind<Func<StoredAttachmentObject,byte[]>>(reopened,"DecryptAttachment")(reopened.Loaded.AttachmentObjects[0]);try{VaultChecks.Require(plaintext.SequenceEqual(original),"Root key survives fresh per-process vault wrapping key");}finally{CryptographicOperations.ZeroMemory(plaintext);}
                ModelPreservation(reopened.Loaded);
                var exported=path+"-backup.vault";reopened.ExportCommitted(exported);VaultChecks.Require(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(Path.Combine(path,"current.vault"))),"Committed backup contains exact root wrap and every retained object");
                var copy=Path.Combine(path,"independent-copy");var candidate=EncryptedVault.ImportEncryptedCopy(copy,exported,secret);VaultChecks.Require(EncryptedVault.InspectCandidates(copy,secret).Any(c=>c.Name==candidate),"Import/Inspect validate complete envelope2 contents with temporary-root disposal");
                using(var recovered=EncryptedVault.Open(copy,secret,candidate)){VaultChecks.Require(!Anchored(recovered),"Pending recovery candidate is not committed-current anchor");VaultChecks.ExpectFailure(()=>Bind<Seal>(recovered,"EncryptAttachment")([1],"not-yet.bin","application/octet-stream"),"Recovered candidate must commit before new object encryption");recovered.Save(recovered.Loaded);VaultChecks.Require(Anchored(recovered),"Explicit candidate-to-current commit anchors recovered root");var recoveredPlain=Bind<Func<StoredAttachmentObject,byte[]>>(recovered,"DecryptAttachment")(recovered.Loaded.AttachmentObjects[0]);try{VaultChecks.Require(recoveredPlain.SequenceEqual(original),"Independent-root one-file backup restores actual original attachment bytes");}finally{CryptographicOperations.ZeroMemory(recoveredPlain);}}
                File.Delete(exported); // Synthetic test output only.
            }
            await ReservationAndFaults(Path.Combine(path,"faults"),secret,legacy,oldBytes,original);
            await RootPreparationFailureKeepsLegacy(Path.Combine(path,"root-prepare-failure"),secret,legacy);
            await CoordinatorRoot(Path.Combine(path,"coordinator"),secret,legacy);
            await CoordinatorReentrantRoot(Path.Combine(path,"reentrant-root"),secret,legacy);
            await CoordinatorLockDuringRoot(Path.Combine(path,"root-lock"),secret,legacy);
            await CoordinatorHiddenDuringRoot(Path.Combine(path,"hidden-root-lock"),secret,legacy);
            await RootPreparationRetry(Path.Combine(path,"root-retry"),secret,legacy);
            await LegacyMigrations(Path.Combine(path,"old-schemas"),secret);
            await ConcealBeforeGateWait(Path.Combine(path,"conceal-gate"),secret,legacy);
            await InFlightUseRevocation(Path.Combine(path,"crypto-revoke"),secret,legacy);
            Console.WriteLine("PASS: private root3-wrap attempt reservation, actual current fingerprint anchor, prepare/flush/lock/failure guards, schema5 carry/history/mode/batch/duplicate, recovery-only restart and full independent-root encrypted backup (no attachment UI)");
        }
        finally{CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(original);if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    private static void ModelPreservation(VaultSnapshot initial)
    {
        var workspace=new EditingWorkspace(TimeProvider.System,initial);var note=workspace.Notes.Single();var current=initial.Notes.Single().AttachmentIds;var past=initial.History.Single();string objects=JsonSerializer.Serialize(initial.AttachmentObjects);
        void Retained(){var snapshot=workspace.Capture();VaultChecks.Require(snapshot.SchemaVersion==5&&snapshot.AttachmentRootId==initial.AttachmentRootId&&JsonSerializer.Serialize(snapshot.AttachmentObjects)==objects,"Model retains complete root/object state");VaultChecks.Require(snapshot.Notes.Where(n=>n.NoteId==note.Id).Single().AttachmentIds.SequenceEqual(current),"Current draft retains exact attachment refs");}
        Retained();note.Title="changed title";note.Favorite=true;Retained();var changed=workspace.Capture();VaultChecks.Require(changed.History.Last().AttachmentIds.SequenceEqual(current),"Ordinary draft changes carry immutable historical attachment refs");workspace.AcceptPrepared(changed);
        var duplicate=workspace.Duplicate(note);VaultChecks.Require(workspace.Capture().Notes.Single(n=>n.NoteId==duplicate.Id).AttachmentIds.SequenceEqual(current),"Duplicate shares immutable object references");
        workspace.ConvertMode(note,"rich",true);workspace.ConvertMode(note,"markdown",true);workspace.ConvertMode(note,"plain",true);Retained();
        workspace.DeleteNotes([note,duplicate]);workspace.RestoreNotes([note,duplicate]);Retained();VaultChecks.Require(workspace.Capture().History.Where(r=>r.NoteId==duplicate.Id).All(r=>r.AttachmentIds.SequenceEqual(current)),"Batch metadata/trash transitions retain refs in every immutable revision");
        long version=note.ContentVersion;workspace.RestoreRevision(note,past.RevisionId);current=past.AttachmentIds;Retained();VaultChecks.Require(note.ContentVersion>version,"Explicit restore of complete attachment refs changes content authority");
        var accepted=workspace.Capture();workspace.AcceptPrepared(accepted);note.Title="unsaved hidden";var hidden=workspace.Capture();var resumed=new EditingWorkspace(TimeProvider.System,workspace.FrozenBasis,hidden);VaultChecks.Require(JsonSerializer.Serialize(resumed.Capture().AttachmentObjects)==objects&&resumed.Capture().Notes.Single(n=>n.NoteId==note.Id).AttachmentIds.SequenceEqual(current),"Frozen basis/displayed hidden recovery carries all objects/refs/history");resumed.Clear();workspace.Clear();VaultChecks.Require(workspace.FrozenBasis.AttachmentObjects.Length==0&&workspace.FrozenBasis.AttachmentRootId==Guid.Empty,"Revocation drops all model root/object refs");
    }
    private static async Task ReservationAndFaults(string path,byte[] secret,VaultSnapshot legacy,byte[] originalBytes,byte[] original)
    {
        Directory.CreateDirectory(path);var countPath=Path.Combine(path,"counter");using(var first=EncryptedVault.Create(countPath,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(countPath,secret))
        {
            var next=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot")(legacy);var counter=typeof(EncryptedVault).GetField("wraps",Private)!;var sequence=typeof(EncryptedVault).GetField("sequence",Private)!;var keyField=typeof(EncryptedVault).GetField("vaultKey",Private)!;
            ulong before=(ulong)counter.GetValue(vault)!;var owned=(byte[])keyField.GetValue(vault)!;keyField.SetValue(vault,new byte[31]);
            try{VaultChecks.ExpectFailure(()=>vault.Prepare(next),"Synthetic crypto failure after attempt reservation");}finally{keyField.SetValue(vault,owned);}
            VaultChecks.Require((ulong)counter.GetValue(vault)! ==before+3,"Failed preparation reserves all3 wrapping attempts");var retry=vault.Prepare(next);VaultChecks.Require((ulong)counter.GetValue(vault)! ==before+6,"Retry reserves another3 wraps");
            ulong seq=(ulong)sequence.GetValue(vault)!;counter.SetValue(vault,VaultEnvelope.MaxWraps-2);VaultChecks.ExpectFailure(()=>vault.Prepare(next),"Less than3 remaining wraps refuses before allocation/encryption");VaultChecks.Require((ulong)counter.GetValue(vault)! ==VaultEnvelope.MaxWraps-2&&(ulong)sequence.GetValue(vault)! ==seq,"Budget refusal preserves counters and expected base");counter.SetValue(vault,before+6);vault.Commit(retry);
        }
        var fault=Path.Combine(path,"flush-fault");Directory.CreateDirectory(fault);File.WriteAllBytes(Path.Combine(fault,"current.vault"),originalBytes);
        using(var vault=EncryptedVault.Open(fault,secret,files:new VaultFailureChecks.FaultFiles("pre-flush")))
        {var next=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot")(legacy);var prepared=vault.Prepare(next);VaultChecks.ExpectFailure(()=>vault.Commit(prepared),"First anchor flush failure");VaultChecks.Require(!Anchored(vault)&&File.ReadAllBytes(Path.Combine(fault,"current.vault")).SequenceEqual(originalBytes),"Failed first anchor never reports authority or replaces original bytes");VaultChecks.ExpectFailure(()=>Bind<Seal>(vault,"EncryptAttachment")(original,"blocked.bin","application/octet-stream"),"Faulted first anchor cannot encrypt objects");}
        var delayed=Path.Combine(path,"delayed-lock");Directory.CreateDirectory(delayed);File.WriteAllBytes(Path.Combine(delayed,"current.vault"),originalBytes);using var files=new BlockingFlush();
        using(var vault=EncryptedVault.Open(delayed,secret,files:files))
        {
            var next=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot")(legacy);var owned=OwnedRoot(vault);var prepared=vault.Prepare(next);var task=Task.Run(()=>vault.Commit(prepared));
            try{VaultChecks.Require(files.Entered.Wait(TimeSpan.FromSeconds(10)),"Blocked first anchor reached ciphertext flush");VaultChecks.Require(!Anchored(vault),"Flush/pending alone is not anchor");vault.ReleaseKeys();VaultChecks.Require(owned.All(b=>b==0),"Root owner zeroing does not wait for ciphertext I/O");}
            finally{files.Continue.Set();await task;}
            VaultChecks.Require(!Anchored(vault),"Late successful commit cannot revive released root authority");
        }
        using(var reopened=EncryptedVault.Open(delayed,secret))VaultChecks.Require(Anchored(reopened),"Settled successful current can be explicitly reopened with recovery secret");
    }
    private static async Task CoordinatorRoot(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(path,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
        {
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;var note=session.Workspace.Notes.Single();note.Title="latest before anchor";
            VaultChecks.Require(await (Task<bool>)enable.Invoke(session,null)!,"Coordinator root migration reports success only after real current commit");VaultChecks.Require(session.Workspace.Capture().SchemaVersion==5&&session.Workspace.Capture().Notes.Single().Title==note.Title&&!session.IsDirty,"Root initialization includes latest note/history and updates full model basis");
            var attachment=Bind<Seal>(vault,"EncryptAttachment")([1,2,3],"held-key.bin","application/octet-stream");
            note.Title=new string('x',257);await session.LockAsync();VaultChecks.Require(session.PendingKind=="plaintext-hidden","Invalid draft retains hidden full schema5 snapshot/root owner");
            VaultChecks.Require(!session.KeysReleased,"Hidden recovery retains keys deliberately");VaultChecks.ExpectFailure(()=>Bind<Seal>(vault,"EncryptAttachment")([1],"locked.bin","application/octet-stream"),"Locked hidden recovery must revoke object encryption even while holding recovery keys");VaultChecks.ExpectFailure(()=>Bind<Func<StoredAttachmentObject,byte[]>>(vault,"DecryptAttachment")(attachment),"Locked hidden recovery must not return attachment plaintext with retained recovery keys");
            var hiddenField=typeof(SaveCoordinator).GetField("hiddenPlaintext",Private)!;var frozen=(VaultSnapshot)hiddenField.GetValue(session)!;var mismatched=frozen with{AttachmentRootId=Guid.NewGuid()};hiddenField.SetValue(session,mismatched);
            VaultChecks.ExpectFailure(()=>session.ResumeHidden(secret),"Mismatched hidden root must fail before re-exposing any workspace");VaultChecks.Require(session.IsLocked&&session.PendingKind=="plaintext-hidden"&&ReferenceEquals(hiddenField.GetValue(session),mismatched),"Failed hidden-root recovery retains its frozen data for explicit repair");hiddenField.SetValue(session,frozen);
            var downgraded=frozen with{SchemaVersion=4,AttachmentRootId=Guid.Empty,AttachmentObjects=[]};hiddenField.SetValue(session,downgraded);
            VaultChecks.ExpectFailure(()=>session.ResumeHidden(secret),"Anchored hidden recovery cannot bypass root ownership by missing-root/schema4 downgrade");VaultChecks.Require(session.IsLocked&&session.PendingKind=="plaintext-hidden"&&ReferenceEquals(hiddenField.GetValue(session),downgraded),"Rejected hidden downgrade keeps frozen state without exposing drafts");hiddenField.SetValue(session,frozen);
            session.ResumeHidden(secret);note=session.Workspace.Notes.Single();note.Title="corrected";VaultChecks.Require(await session.SaveAsync(),"Corrected hidden schema5 snapshot saves with same private root");await session.LockAsync();
        }
        using(var reopened=EncryptedVault.Open(path,secret))VaultChecks.Require(reopened.Loaded.SchemaVersion==5&&reopened.Loaded.Notes.Single().Title=="corrected","Coordinator hidden/root migration survives real encrypted restart");
    }
    private static async Task CoordinatorHiddenDuringRoot(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);using var files=new BlockingFlush();
        using(var vault=EncryptedVault.Open(path,secret,files:files))using(var session=new SaveCoordinator(vault,TimeProvider.System))
        {
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;var anchor=(Task<bool>)enable.Invoke(session,null)!;Task<bool>? locked=null;
            try{VaultChecks.Require(files.Entered.Wait(TimeSpan.FromSeconds(10)),"First root commit is blocked");session.Workspace.Notes.Single().Title=new string('q',257);locked=session.LockAsync();VaultChecks.Require(session.IsLocked&&!session.KeysReleased&&session.PendingKind=="plaintext-hidden","Dirty invalid draft keeps hidden recovery keys during root I/O");}
            finally{files.Continue.Set();}
            VaultChecks.Require(!await anchor&&locked is not null&&!await locked,"Root initialization cannot report active success after hidden lock");
            VaultChecks.ExpectFailure(()=>Bind<Seal>(vault,"EncryptAttachment")([1],"late-locked.bin","application/octet-stream"),"Late committed root must not grant attachment authority while hidden/locked with keys held");
            session.ResumeHidden(secret);session.Workspace.Notes.Single().Title="resumed";VaultChecks.Require(await session.SaveAsync(),"Verified hidden resume can repair and save same-root state");var enabled=Bind<Seal>(vault,"EncryptAttachment")([1],"resumed.bin","application/octet-stream");VaultChecks.Require(enabled.RootId==session.Workspace.Capture().AttachmentRootId,"Exact-secret successful hidden resume explicitly restores attachment authority");await session.LockAsync();
        }
    }
    private static async Task CoordinatorReentrantRoot(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using var files=new BlockingFlush();using(var session=new SaveCoordinator(EncryptedVault.Open(path,secret,files:files),TimeProvider.System))
        {
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;Task<bool>? nested=null;bool handled=false;
            session.Changed+=()=>{if(!handled&&session.Status=="첨부 저장 준비 중"){handled=true;nested=(Task<bool>)enable.Invoke(session,null)!;}};
            var outer=(Task<bool>)enable.Invoke(session,null)!;bool pendingJoin=handled&&ReferenceEquals(nested,outer);
            files.Continue.Set();bool success=await outer;
            VaultChecks.Require(pendingJoin&&success,$"Reentrant root initialization must join one reserved task/commit while flush is blocked: handled={handled},same={ReferenceEquals(nested,outer)},success={success}");
            await session.LockAsync();
        }
    }
    private static async Task RootPreparationFailureKeepsLegacy(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(path,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
        {
            var note=session.Workspace.Notes.Single();note.Title="normal unsaved legacy edit";var initialize=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot");initialize(session.Workspace.Capture());var abandoned=OwnedRoot(vault);
            var wraps=typeof(EncryptedVault).GetField("wraps",Private)!;wraps.SetValue(vault,VaultEnvelope.MaxWraps-2);string before=JsonSerializer.Serialize(session.Workspace.FrozenBasis);long version=note.EditVersion;var original=File.ReadAllBytes(Path.Combine(path,"current.vault"));
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;VaultChecks.Require(!await (Task<bool>)enable.Invoke(session,null)!,"3-wrap root preparation must refuse with only2 remaining");
            VaultChecks.Require(JsonSerializer.Serialize(session.Workspace.FrozenBasis)==before&&note.EditVersion==version&&note.Title=="normal unsaved legacy edit"&&session.Workspace.Capture().SchemaVersion==4&&File.ReadAllBytes(Path.Combine(path,"current.vault")).SequenceEqual(original),"Failed root activation leaves full legacy basis/draft/version/data unchanged (Capture gives dirty drafts fresh uncommitted revision IDs)");
            VaultChecks.Require(await session.SaveAsync(),"Failed first root preparation must not block still-valid legacy2-wrap save");
            VaultChecks.Require(abandoned.All(b=>b==0)&&!Anchored(vault),"Failed unprepared root is zeroed/forgotten without false anchor");await session.LockAsync();VaultChecks.Require(session.PendingKind=="none","Normal legacy lock remains possible after root activation refusal");
        }
        using(var reopened=EncryptedVault.Open(path,secret))VaultChecks.Require(reopened.Loaded.SchemaVersion==4&&reopened.Loaded.Notes.Single().Title=="normal unsaved legacy edit","Unsuccessful root activation does not permanently migrate or lose ordinary edits");
    }
    private static async Task RootPreparationRetry(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(path,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
        {
            var candidate=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot")(session.Workspace.Capture());var abandoned=OwnedRoot(vault);
            var keyField=typeof(EncryptedVault).GetField("vaultKey",Private)!;var key=(byte[])keyField.GetValue(vault)!;var counter=typeof(EncryptedVault).GetField("wraps",Private)!;ulong before=(ulong)counter.GetValue(vault)!;
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;keyField.SetValue(vault,new byte[31]);
            try{VaultChecks.Require(!await (Task<bool>)enable.Invoke(session,null)!,"Transient synthetic crypto failure refuses first root preparation");}finally{keyField.SetValue(vault,key);}
            VaultChecks.Require(abandoned.All(b=>b==0)&&session.Workspace.Capture().SchemaVersion==4&&(ulong)counter.GetValue(vault)! ==before+3,"Abandoned unprepared root is zeroed while failed wrapping reservation remains counted");
            VaultChecks.Require(await (Task<bool>)enable.Invoke(session,null)!&&session.Workspace.Capture().AttachmentRootId!=candidate.AttachmentRootId&&(ulong)counter.GetValue(vault)! ==before+6,"Explicit retry creates a fresh root and counts3 more attempts without losing legacy state");await session.LockAsync();
        }
    }
    private static async Task LegacyMigrations(string path,byte[] secret)
    {
        var now=DateTimeOffset.UtcNow;var noteId=Guid.NewGuid();var historyId=Guid.NewGuid();
        for(int version=1;version<=4;version++)
        {
            var snapshot=new VaultSnapshot(version,Guid.NewGuid(),[new(noteId,Guid.NewGuid(),[historyId],now,now,"synthetic old","old body")]){History=[new(noteId,historyId,[],now,"old history","old text")]};
            var json=System.Text.Json.JsonSerializer.SerializeToNode(snapshot,SnapshotSerialization.Options(snapshot.SchemaVersion))!.AsObject();Schema2Checks.StripAttachmentFields(json);
            if(version<4)Schema2Checks.StripDocumentFields(json);if(version<3)json.Remove("uiDevices");
            if(version==1){json.Remove("folders");json.Remove("tags");foreach(var note in json["notes"]!.AsArray())note!.AsObject().Remove("metadata");foreach(var revision in json["history"]!.AsArray())revision!.AsObject().Remove("metadata");}
            var current=Path.Combine(path,"v"+version);Directory.CreateDirectory(current);var bytes=Schema2Checks.Encode(json,secret);File.WriteAllBytes(Path.Combine(current,"current.vault"),bytes);
            using(var session=new SaveCoordinator(EncryptedVault.Open(current,secret),TimeProvider.System))
            {var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;VaultChecks.Require(await (Task<bool>)enable.Invoke(session,null)!,"First bounded attachment root migration from each actual old-shaped schema");VaultChecks.Require(session.Workspace.Capture().Notes.Single().RevisionId==snapshot.Notes.Single().RevisionId&&session.Workspace.Capture().History.Single().RevisionId==historyId,"Schema migration retains original note/history IDs and complete payload");await session.LockAsync();}
            VaultChecks.Require(Directory.GetFiles(current,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(bytes)),"Each old-shaped schema1-4 retains exact original ciphertext previous");
            using(var restored=EncryptedVault.Open(current,secret))VaultChecks.Require(restored.Loaded.SchemaVersion==5&&restored.Loaded.Notes.Single().Text=="old body"&&restored.Loaded.History.Single().Text=="old text","Actual recovery-only restart after old-shaped envelope migration");
        }
    }
    private static async Task ConcealBeforeGateWait(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(path,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))using(var concealed=new ManualResetEventSlim(false))
        {
            session.Conceal+=()=>concealed.Set();var gate=typeof(EncryptedVault).GetField("gate",Private)!.GetValue(vault)!;Task<bool> locked;bool immediate;
            Monitor.Enter(gate);
            try{locked=Task.Run(()=>session.LockAsync());immediate=concealed.Wait(TimeSpan.FromSeconds(1));}
            finally{Monitor.Exit(gate);}
            await locked;VaultChecks.Require(immediate&&session.KeysReleased,"Conceal callback must precede any vault gate/key-release wait; attachment revoke cannot block native hiding");
        }
    }
    private static async Task InFlightUseRevocation(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);
        using(var vault=EncryptedVault.Open(path,secret))
        {
            var snapshot=Bind<Func<VaultSnapshot,VaultSnapshot>>(vault,"InitializeAttachmentRoot")(legacy);vault.Save(snapshot);
            var original=RandomNumberGenerator.GetBytes(4194304);var seal=Bind<Seal>(vault,"EncryptAttachment");var open=Bind<Func<StoredAttachmentObject,byte[]>>(vault,"DecryptAttachment");var item=seal(original,"large synthetic.bin","application/octet-stream");
            try
            {
                bool sealRejected=await RevokedWhileBusy(()=>seal(original,"late synthetic.bin","application/octet-stream"));Resume();
                bool openRejected=await RevokedWhileBusy(()=>open(item));
                VaultChecks.Require(sealRejected&&openRejected,"A root-use revoke during bounded synchronous crypto must refuse late ciphertext metadata/plaintext returns, including zeroing failed owned output");
            }
            finally{CryptographicOperations.ZeroMemory(original);}
            void Resume()=>typeof(EncryptedVault).GetMethod("ResumeAttachmentUse",Private)!.Invoke(vault,[secret,snapshot]);
            async Task<bool> RevokedWhileBusy<T>(Func<T> call)
            {
                var gate=typeof(EncryptedVault).GetField("gate",Private)!.GetValue(vault)!;var task=Task.Run(call);bool busy=false;var timer=System.Diagnostics.Stopwatch.StartNew();
                while(!task.IsCompleted&&timer.Elapsed<TimeSpan.FromSeconds(10))
                {if(!Monitor.TryEnter(gate)){busy=true;break;}Monitor.Exit(gate);Thread.Yield();}
                if(busy)typeof(EncryptedVault).GetMethod("RevokeAttachmentUse",Private)!.Invoke(vault,null);
                try{var returned=await task;if(returned is byte[] bytes)CryptographicOperations.ZeroMemory(bytes);return false;}catch(InvalidOperationException){return busy;}
            }
        }
    }
    private static async Task CoordinatorLockDuringRoot(string path,byte[] secret,VaultSnapshot legacy)
    {
        using(var first=EncryptedVault.Create(path,secret,secret))first.Save(legacy);using var files=new BlockingFlush();
        using(var session=new SaveCoordinator(EncryptedVault.Open(path,secret,files:files),TimeProvider.System))
        {
            var enable=typeof(SaveCoordinator).GetMethod("EnsureAttachmentRootAsync",Private)!;Task<bool>? lockTask=null;bool concealed=false;
            session.Conceal+=()=>concealed=true;session.Changed+=()=>{if(lockTask is null&&session.Status=="첨부 저장 준비 중")lockTask=session.LockAsync();};
            var anchor=(Task<bool>)enable.Invoke(session,null)!;
            try{VaultChecks.Require(files.Entered.Wait(TimeSpan.FromSeconds(10))&&concealed&&session.IsLocked&&session.KeysReleased,"Reentrant lock conceals and zeroes root without waiting for its blocked ciphertext commit");}
            finally{files.Continue.Set();}
            VaultChecks.Require(!await anchor&&lockTask is not null&&await lockTask,"Late root commit cannot report active authority after reentrant lock; jobs settle before dispose");
        }
    }
    private sealed class BlockingFlush:IAtomicVaultFiles,IDisposable
    {
        private readonly AtomicVaultFiles actual=new();internal readonly ManualResetEventSlim Entered=new(false),Continue=new(false);
        public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){Entered.Set();Continue.Wait();actual.FlushToDisk(stream);}public void Replace(string temp,string current,string previous)=>actual.Replace(temp,current,previous);public void Move(string temp,string current)=>actual.Move(temp,current);public void Dispose(){Entered.Dispose();Continue.Dispose();}
    }
}

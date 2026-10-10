using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class Schema10ImageChecks
{
    internal static async Task Run()
    {
        byte[] bytes=File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"tests/fixtures/storage/frozen-schema9.json"));
        VaultChecks.Require(bytes.Length==2710&&Convert.ToHexStringLower(SHA256.HashData(bytes))=="cde32f87ced6cbf5828c2c6ad3547e23243588aa719d727cd963fc6945518050","Actual pre-H01 binary-written schema9 exact frozen bytes");
        var nine=VaultEnvelope.ReadSnapshot(bytes);VaultChecks.Require(SnapshotSerialization.Bytes(nine).SequenceEqual(bytes),"Frozen pre10 schema9 exact roundtrip");VaultEnvelope.Validate(nine with{SchemaVersion=10});
        await Lifecycle();await FailedMigration(false);await FailedMigration(true);
        Console.WriteLine("PASS: schema10 real envelope/root/three-wrap/sticky migration, exact prechange9 bytes and previous cipher, PNG authority, current/locked/selected/whole backup, write faults and pending recovery");
    }
    private static async Task Lifecycle()
    {
        string home=Path.Combine(Path.GetTempPath(),"memo-image10-lifecycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);string root=Path.Combine(home,"vault"),backup=Path.Combine(home,"manual.vault"),locked=Path.Combine(home,"locked.vault");byte[] secret=EncryptedVault.GenerateRecoverySecret();Guid id,rootId,noteId;
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Text="before\nafter";session.Workspace.ConvertMode(note,"rich",true);VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Real prepared attachment root before explicit image");
                id=session.AttachBytes(note,SelectedPngChecks.Png,"SYNTHETIC.png","image/png",note.EditVersion);noteId=note.Id;rootId=session.Workspace.Capture().AttachmentRootId;
                Guid profile=Guid.NewGuid();var policy=new StoredAutomaticBackupPolicy(@"C:\SYNTHETIC_BACKUP",Guid.NewGuid(),new(root,1,"00112233445566778899aabbccddeeff"),8,true,false,true);session.Workspace.SetAutomaticBackupPolicy(profile,policy);VaultChecks.Require(await session.SaveAsync(),"Actual schema9 predecessor settled");
                var original=note.Document!;byte[] previous=File.ReadAllBytes(Path.Combine(root,"current.vault"));var wraps=typeof(EncryptedVault).GetField("wraps",BindingFlags.Instance|BindingFlags.NonPublic)!;ulong count=(ulong)wraps.GetValue(vault)!;
                VaultChecks.ExpectFailure(()=>session.InsertInlineImageAsync(note,id,1,note.EditVersion-1).GetAwaiter().GetResult(),"Public insertion rejects stale source version");
                VaultChecks.Require(await session.InsertInlineImageAsync(note,id,1,note.EditVersion)&&session.Workspace.Capture().SchemaVersion==10,"Explicit active authenticated PNG event activates10");var imageDocument=note.Document!;
                var emptyNine=session.Workspace.Capture() with{SchemaVersion=9,Notes=[],History=[]};
                var prepared=vault.Prepare(session.Workspace.Capture());VaultChecks.Require((ulong)wraps.GetValue(vault)! ==count+3,"Schema10 reserves exactly three existing envelope2 wraps");
                VaultChecks.ExpectFailure(()=>vault.Prepare(emptyNine),"Prepared10 refuses9 before commit even without remaining image content");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(emptyNine),"Prepared10 hidden downgrade refused");vault.Commit(prepared);VaultChecks.Require(await session.SaveAsync(),"Coordinator settles image10 after actual prepared commit");
                VaultChecks.Require(Directory.GetFiles(root,"previous-*.vault").Any(path=>File.ReadAllBytes(path).SequenceEqual(previous)),"Original real9 predecessor ciphertext retained exact");
                VaultChecks.Require(session.Workspace.HistoryFor(note).Any(h=>h.Document==original)&&note.Text=="before\n[이미지: SYNTHETIC.png]\nafter","Complete original v1 source and exact new projection");Schema10ImageHiddenChecks.RequirePng(session,note,id);
                VaultChecks.ExpectFailure(()=>session.Workspace.ConvertMode(note,"plain"),"Image mode conversion requires existing explicit loss acknowledgement");session.Workspace.ConvertMode(note,"plain",true);VaultChecks.Require(note.Document is null&&session.Workspace.HistoryFor(note).Any(h=>h.Document==imageDocument)&&session.Workspace.Capture().SchemaVersion==10,"Confirmed conversion preserves complete image predecessor and sticky10");
                session.Workspace.RestoreRevision(note,session.Workspace.HistoryFor(note).First(h=>h.Document==imageDocument).RevisionId);VaultChecks.Require(note.Document==imageDocument,"Image historical restore retains exact source");
                session.RemoveInlineImage(note,1,note.EditVersion);VaultChecks.Require(note.Document!.SchemaVersion==2&&note.AttachmentIds.Contains(id)&&session.Workspace.Capture().AttachmentObjects.Any(item=>item.ObjectId==id),"Actual last image removal leaves v2 independent ref and immutable object");
                session.DetachAttachment(note,id,note.EditVersion);VaultChecks.Require(!note.AttachmentIds.Contains(id)&&session.Workspace.Capture().History.Any(h=>h.AttachmentIds.Contains(id))&&session.Workspace.Capture().AttachmentObjects.Any(item=>item.ObjectId==id),"Explicit independent detach does not garbage collect historical authenticated PNG");
                session.Workspace.RestoreRevision(note,session.Workspace.HistoryFor(note).First(h=>h.Document==imageDocument).RevisionId);Schema10ImageHiddenChecks.RequirePng(session,note,id);
                session.Workspace.SetAutomaticBackupPolicy(profile,policy with{Capacity=9});session.Workspace.SetAutomaticBackupPolicy(profile,null);VaultChecks.Require(session.Workspace.Capture().SchemaVersion==10&&await session.SaveAsync(),"Policy changes preserve10 through actual encrypted save");
                VaultChecks.Require(await session.BackupAsync(backup),"Manual encrypted image10 archive");byte[] archive=File.ReadAllBytes(backup);
                try
                {
                    var copy=session.ImportSelectedEncryptedBackup(archive,[note.Id],session.AttachmentPreviewEpoch).Single();VaultChecks.Require(copy.Id!=note.Id&&copy.Document==imageDocument&&session.Workspace.Capture().SchemaVersion==10,"Selected image10 import preserves complete source and record-local PNG IDs");Schema10ImageHiddenChecks.RequirePng(session,copy,id);
                }
                finally{CryptographicOperations.ZeroMemory(archive);}
                VaultChecks.Require(await session.LockWithBackupAsync(locked)&&session.KeysReleased,"Locked committed-copy10 backup releases keys");
                VaultChecks.ExpectFailure(()=>session.InsertInlineImageAsync(note,id,1,note.EditVersion).GetAwaiter().GetResult(),"Public image insertion rejects locked source");
            }
            using(var reopened=EncryptedVault.Open(root,secret))using(var session=new SaveCoordinator(reopened,TimeProvider.System))
            {
                var note=session.Workspace.Notes.Single(n=>n.Id==noteId);VaultChecks.Require(reopened.Loaded.SchemaVersion==10&&reopened.Loaded.AttachmentRootId==rootId&&RichDocumentCodec.Images(note.Document!).Single().AttachmentId==id,"Actual encrypted image10 restart retains exact placement and root");Schema10ImageHiddenChecks.RequirePng(session,note,id);
                var lower=reopened.Loaded with{SchemaVersion=9,Notes=[],History=[]};VaultChecks.ExpectFailure(()=>reopened.Prepare(lower),"Loaded10 refuses9 after restart");VaultChecks.ExpectFailure(()=>reopened.InitializeAttachmentRoot(lower),"Loaded10 root helper refuses9");
                VaultChecks.ExpectFailure(()=>VaultEnvelope.Decrypt(Schema2Checks.Encode(System.Text.Json.JsonSerializer.SerializeToNode(reopened.Loaded,SnapshotSerialization.Options(10))!.AsObject(),secret),secret),"Envelope1 cannot authenticate payload10/root2 semantics");VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(reopened.Loaded with{SchemaVersion=11}),"Future payload11 refused");VaultChecks.Require(await session.LockAsync(),"Image10 restart lock");
            }
            foreach(string archive in new[]{backup,locked})
            {
                string restored=Path.Combine(home,Path.GetFileName(archive)+"-restored");string candidate=EncryptedVault.ImportEncryptedCopy(restored,archive,secret);
                using var vault=EncryptedVault.Open(restored,secret,candidate);vault.Save(vault.Loaded);using var session=new SaveCoordinator(vault,TimeProvider.System);
                VaultChecks.Require(vault.Loaded.SchemaVersion==10&&vault.Loaded.AttachmentRootId==rootId,"Whole image10 archive retains original root ownership");var note=session.Workspace.Notes.Single(n=>n.Id==noteId);Schema10ImageHiddenChecks.RequirePng(session,note,id);VaultChecks.Require(await session.LockAsync(),"Whole image10 archive lock");
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(home,true);}
    }
    private static async Task FailedMigration(bool replace)
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-image10-fault-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();var files=new ImageFaultFiles();Guid id;StyledDocument source;
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret,files))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Text="SYNTHETIC fault source";session.Workspace.ConvertMode(note,"rich",true);VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Image fault root baseline");id=session.AttachBytes(note,SelectedPngChecks.Png,"SYNTHETIC.png","image/png",note.EditVersion);
                var basis=session.Workspace.Capture() with{SchemaVersion=9};vault.Save(basis);session.Workspace.AcceptPrepared(basis);VaultChecks.Require(await session.SaveAsync(),"Image fault predecessor settled");byte[] previous=File.ReadAllBytes(Path.Combine(root,"current.vault"));
                VaultChecks.Require(await session.InsertInlineImageAsync(note,id,1,note.EditVersion),"Image fault activation");source=note.Document!;files.ReplaceFailure=replace;files.FlushFailure=!replace;
                VaultChecks.Require(!await session.SaveAsync()&&session.IsDirty&&note.Document==source&&previous.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Failed image10 write preserves exact9 current ciphertext and complete dirty source");
                VaultChecks.Require(!await session.LockAsync()&&session.KeysReleased,"Prepared image10 write failure releases keys and retains recovery candidate");
            }
            string pending=Directory.GetFiles(root,"pending-*.vault").Single();
            using(var recovered=EncryptedVault.Open(root,secret,Path.GetFileName(pending)))
            {
                VaultChecks.Require(recovered.Loaded.SchemaVersion==10&&recovered.Loaded.Notes.Single().Document==source,"Actual image10 pending ciphertext authenticates after write failure");recovered.Save(recovered.Loaded);using var session=new SaveCoordinator(recovered,TimeProvider.System);Schema10ImageHiddenChecks.RequirePng(session,session.Workspace.Notes.Single(),id);VaultChecks.Require(await session.LockAsync(),"Recovered pending10 original root decrypts PNG");
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class ImageFaultFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool FlushFailure,ReplaceFailure;
        public Stream CreateNew(string path)=>actual.CreateNew(path);
        public void FlushToDisk(Stream stream){if(FlushFailure)throw new IOException("SYNTHETIC image10 flush fault");actual.FlushToDisk(stream);}
        public void Move(string source,string destination)=>actual.Move(source,destination);
        public void Replace(string source,string destination,string previous){if(ReplaceFailure)throw new IOException("SYNTHETIC image10 replace fault");actual.Replace(source,destination,previous);}
    }
}

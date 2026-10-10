using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class Schema10ImageHiddenChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-hidden-image10-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var vault=EncryptedVault.Create(root,secret,secret);using var session=new SaveCoordinator(vault,TimeProvider.System);
            var note=session.Workspace.CreateNote();note.Text="SYNTHETIC original";session.Workspace.ConvertMode(note,"rich",true);VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Hidden image prepared root");
            Guid attachment=session.AttachBytes(note,SelectedPngChecks.Png,"SYNTHETIC.png","image/png",note.EditVersion);Guid profile=Guid.NewGuid();
            var policy=new StoredAutomaticBackupPolicy(@"C:\SYNTHETIC_BACKUP",Guid.NewGuid(),new(root,ulong.MaxValue,"00112233445566778899aabbccddeeff"),4,false,true,true);
            session.Workspace.SetAutomaticBackupPolicy(profile,policy);VaultChecks.Require(await session.SaveAsync(),"Authentic schema9 predecessor");byte[] previous=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            var before=session.Workspace.Capture();string history=JsonSerializer.Serialize(before.History),objects=JsonSerializer.Serialize(before.AttachmentObjects);Guid attachmentRoot=before.AttachmentRootId;
            VaultChecks.Require(await session.InsertInlineImageAsync(note,attachment,1,note.EditVersion),"Explicit unprepared10 image placement");var document=note.Document;note.Title=new string('t',257);
            VaultChecks.Require(!await session.LockAsync()&&session.IsLocked&&!session.KeysReleased,"Repairable title rejects preparation but retains hidden unprepared10 authority");
            VaultChecks.Require(previous.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Unprepared10 invalid title preserves exact9 ciphertext");
            session.ResumeHidden(secret);note=session.Workspace.Notes.Single();var resumed=session.Workspace.Capture();
            VaultChecks.Require(note.Title.Length==257&&note.Document==document&&resumed.SchemaVersion==10&&resumed.AttachmentRootId==attachmentRoot&&session.Workspace.GetUiDevice(profile).AutomaticBackupPolicy==policy&&JsonSerializer.Serialize(resumed.AttachmentObjects)==objects&&resumed.DiscardedRevisions.SequenceEqual(before.DiscardedRevisions),"Hidden10 permits repairing title while exact document/root/objects/policy/witnesses remain");
            VaultChecks.Require(resumed.History.Take(before.History.Length).Select(h=>h.Document).SequenceEqual(before.History.Select(h=>h.Document)),"Hidden10 preserves original immutable histories");
            var image=RichDocumentCodec.Images(note.Document!).Single();VaultChecks.Require(image.AttachmentId==attachment,"Hidden10 active image authority retained");RequirePng(session,note,attachment);
            vault.ValidateHiddenRoot(resumed);VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(resumed with{Notes=[resumed.Notes[0] with{AttachmentIds=[]}]}),"Hidden10 rejects image authority corruption independently of repairable title");
            note.Title="SYNTHETIC repaired";VaultChecks.Require(await session.SaveAsync()&&await session.LockAsync(),"Explicit title repair encrypts10 and releases keys");
            using(var opened=VaultEnvelope.DecryptOwned(File.ReadAllBytes(Path.Combine(root,"current.vault")),secret))VaultChecks.Require(opened.Snapshot.SchemaVersion==10&&opened.Snapshot.Notes.Single().Document==document,"Actual repaired10 ciphertext authenticates exact source");
            Console.WriteLine("PASS: schema10 repairable invalid-title hidden recovery, exact image/source/policy/root/history, separate image-authority rejection and repaired encrypted save");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    internal static void RequirePng(SaveCoordinator session,NoteDraft note,Guid id)
    {byte[] read=session.ReadAttachmentBytes(note,id,note.EditVersion);try{VaultChecks.Require(read.SequenceEqual(SelectedPngChecks.Png),"Authenticated original PNG bytes survive image document lifecycle");}finally{CryptographicOperations.ZeroMemory(read);}}
}

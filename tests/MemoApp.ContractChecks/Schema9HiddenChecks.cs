using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class Schema9HiddenChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-hidden9-"+Guid.NewGuid().ToString("N"));
        string backup=Path.Combine(Path.GetTempPath(),"memo-copy9-"+Guid.NewGuid().ToString("N")+".vault");
        string restoredRoot=Path.Combine(Path.GetTempPath(),"memo-restored9-"+Guid.NewGuid().ToString("N"));
        byte[] secret=RandomNumberGenerator.GetBytes(32),source="SYNTHETIC_SCHEMA9_ATTACHMENT"u8.ToArray();Guid attachmentId,actualRoot,profile;
        var fixture=VaultEnvelope.ReadSnapshot(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),"tests/fixtures/storage/frozen-schema8.json")));
        try
        {
            using(var initial=EncryptedVault.Create(root,secret,secret))
            {
                actualRoot=initial.InitializeAttachmentRoot(initial.Loaded).AttachmentRootId;
                initial.Save(fixture with{AttachmentRootId=actualRoot});
            }
            using(var vault=EncryptedVault.Open(root,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.Notes.Single();profile=fixture.UiDevices[0].UiDeviceId;
                attachmentId=session.AttachBytes(note,source,"SYNTHETIC.bin","application/octet-stream",note.EditVersion);
                VaultChecks.Require(await session.SaveAsync()&&session.Workspace.Capture().SchemaVersion==8,"Real schema8 attachment baseline before unprepared9");
                byte[] previous=File.ReadAllBytes(Path.Combine(root,"current.vault"));string evidence=JsonSerializer.Serialize(session.Workspace.Capture().DiscardedRevisions);
                var policy=new StoredAutomaticBackupPolicy(@"C:\SYNTHETIC_MISSING_BACKUP",Guid.NewGuid(),new(root,ulong.MaxValue,"00112233445566778899aabbccddeeff"),4,false,true,true,new DateOnly(2026,10,9));
                session.Workspace.SetAutomaticBackupPolicy(profile,policy);note.Title="SYNTHETIC_LATEST_UNSAVED9";
                var wraps=typeof(EncryptedVault).GetField("wraps",BindingFlags.Instance|BindingFlags.NonPublic)!;ulong count=(ulong)wraps.GetValue(vault)!;wraps.SetValue(vault,VaultEnvelope.MaxWraps);
                VaultChecks.Require(!await session.LockAsync()&&session.IsLocked&&!session.KeysReleased,"Unprepared9 budget failure retains hidden recovery authority");
                VaultChecks.Require(previous.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Hidden unprepared9 failure preserves exact original8 cipher");
                session.ResumeHidden(secret);note=session.Workspace.Notes.Single();var hidden=session.Workspace.Capture();
                VaultChecks.Require(hidden.SchemaVersion==9&&hidden.AttachmentRootId==actualRoot&&note.Title=="SYNTHETIC_LATEST_UNSAVED9"&&session.Workspace.GetUiDevice(profile).AutomaticBackupPolicy==policy&&JsonSerializer.Serialize(hidden.DiscardedRevisions)==evidence,"Hidden unprepared9 restores latest source/policy/root/discarded witnesses");
                RequireAttachment(session,note,attachmentId,source);
                wraps.SetValue(vault,count);VaultChecks.Require(await session.SaveAsync(),"Explicit hidden9 correction commits latest state");
                VaultChecks.Require(Directory.GetFiles(root,"previous-*.vault").Any(path=>File.ReadAllBytes(path).SequenceEqual(previous)),"Schema9 save preserves previous real8 ciphertext with original root authority");
                VaultChecks.Require(await session.BackupAsync(backup)&&await session.LockAsync(),"Schema9 committed encrypted archive and explicit lock");
            }
            using(var vault=EncryptedVault.Open(root,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.Notes.Single();VaultChecks.Require(vault.Loaded.SchemaVersion==9&&vault.Loaded.AttachmentRootId==actualRoot&&session.Workspace.GetUiDevice(profile).AutomaticBackupPolicy is not null&&note.Title=="SYNTHETIC_LATEST_UNSAVED9","Actual9 restart retains policy/source/root despite missing external backup directory");RequireAttachment(session,note,attachmentId,source);VaultChecks.Require(await session.LockAsync(),"Restart lock releases9 keys");
            }
            string pending=EncryptedVault.ImportEncryptedCopy(restoredRoot,backup,secret);
            using(var vault=EncryptedVault.Open(restoredRoot,secret,pending))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                vault.Save(vault.Loaded);VaultChecks.Require(vault.Loaded.SchemaVersion==9&&vault.Loaded.AttachmentRootId==actualRoot,"Imported schema9 archive retains original attachment root ownership");RequireAttachment(session,session.Workspace.Notes.Single(),attachmentId,source);VaultChecks.Require(await session.LockAsync(),"Imported9 archive closes safely");
            }
            Console.WriteLine("PASS: schema9 hidden unprepared latest source/policy/evidence, original8 previous ciphertext, actual attachment/root ownership, encrypted9 archive restore and missing-directory restart");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(source);if(Directory.Exists(root))Directory.Delete(root,true);if(Directory.Exists(restoredRoot))Directory.Delete(restoredRoot,true);if(File.Exists(backup))File.Delete(backup);
        }
    }
    private static void RequireAttachment(SaveCoordinator session,NoteDraft note,Guid id,byte[] expected)
    {
        byte[] read=session.ReadAttachmentBytes(note,id,note.EditVersion);try{VaultChecks.Require(read.SequenceEqual(expected),"Actual schema9 attachment ciphertext retains correct original root ownership");}finally{CryptographicOperations.ZeroMemory(read);}
    }
}

using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Search;
internal static class WholeMigrationChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-whole-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            const string opaque="  { \"nodes\" : [ {\"type\":\"future-node\",\"n\":1e+03,\"text\":\"\\uAC00\"} ] }  ";var now=DateTimeOffset.UtcNow;var future=new VaultSnapshot(4,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"합성 opaque","AUTHENTICATED_FUTURE_TEXT","rich"){Document=new(1,opaque)}]);string source=Path.Combine(root,"source");using(var seed=EncryptedVault.Create(source,secret,secret))seed.Save(future);
            using var session=new SaveCoordinator(EncryptedVault.Open(source,secret),TimeProvider.System);var note=session.Workspace.Notes.Single();var folder=session.Workspace.CreateFolder("합성 폴더");session.Workspace.MoveNote(note,folder.FolderId);session.Workspace.SetTags(note,["합성 태그"]);note.Favorite=true;await session.PrepareAttachmentsAsync();session.AttachBytes(note,new byte[]{0,255,1,2,3},"opaque-original.bin","application/octet-stream",note.EditVersion);var profile=Guid.NewGuid();session.Workspace.SetUiPreferences(profile,new(true,18,1.25));session.Workspace.SetWindowLayout(profile,new("memo",note.Id,"synthetic monitor",0.2,0.3,360,400,96));session.Workspace.RecordRecentNote(profile,note);session.Workspace.SaveSearch(profile,"정확한 조건",new SearchOptions{Query="합성",FolderId=folder.FolderId,Tag="합성 태그"});
            string transfer=Path.Combine(root,"all.vault");VaultChecks.Require(await session.BackupAsync(transfer),"Whole committed source exported");byte[] accepted=SnapshotSerialization.Bytes(session.Workspace.Capture());byte[] original=File.ReadAllBytes(Path.Combine(source,"current.vault"));
            byte[] wrong=EncryptedVault.GenerateRecoverySecret();try{VaultChecks.ExpectFailure(()=>EncryptedVault.ImportEncryptedCopy(Path.Combine(root,"wrong"),transfer,wrong),"Wrong recovery secret cannot import whole source");}finally{CryptographicOperations.ZeroMemory(wrong);}
            string destination=Path.Combine(root,"new-pc");string candidate=EncryptedVault.ImportEncryptedCopy(destination,transfer,secret);using var imported=new SaveCoordinator(EncryptedVault.Open(destination,secret,candidate),TimeProvider.System);VaultChecks.Require(SnapshotSerialization.Bytes(imported.Workspace.Capture()).SequenceEqual(accepted),"Every complete authenticated field preserved before fresh-root anchor");VaultChecks.Require(await imported.SaveAsync(),"Fresh PC root committed");VaultChecks.Require(SnapshotSerialization.Bytes(imported.Workspace.Capture()).SequenceEqual(accepted),"Anchor does not change exact whole source");var restored=imported.Workspace.Notes.Single();byte[] attachment=imported.ReadAttachmentBytes(restored,restored.AttachmentIds.Single(),restored.EditVersion);try{VaultChecks.Require(attachment.SequenceEqual(new byte[]{0,255,1,2,3})&&restored.Document!.SourceJson==opaque,"Actually decrypted original and unknown rich source preserved");}finally{CryptographicOperations.ZeroMemory(attachment);}VaultChecks.Require(original.SequenceEqual(File.ReadAllBytes(Path.Combine(source,"current.vault"))),"Migration never modifies or removes original");await imported.LockAsync();await session.LockAsync();
            Console.WriteLine("PASS: whole single encrypted file, exact opaque source/history/folders/tags/profiles/search records and authenticated attachment on fresh root");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class InlineImageAdmissionChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-image10-admission-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();Guid id;byte[] previous;
        try
        {
            using(var vault=EncryptedVault.Create(root,secret,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.CreateNote();note.Text="SYNTHETIC previous source";session.Workspace.ConvertMode(note,"rich",true);VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Admission root baseline");id=session.AttachBytes(note,SelectedPngChecks.Png,"SYNTHETIC.png","image/png",note.EditVersion);VaultChecks.Require(await session.SaveAsync(),"Admission previous source with authenticated PNG");previous=File.ReadAllBytes(Path.Combine(root,"current.vault"));note.Title="SYNTHETIC current";VaultChecks.Require(await session.SaveAsync()&&await session.LockAsync(),"Admission newer current and settled release");
            }
            string candidate=Directory.GetFiles(root,"previous-*.vault").Single(path=>File.ReadAllBytes(path).SequenceEqual(previous));
            using(var vault=EncryptedVault.Open(root,secret,Path.GetFileName(candidate)))using(var session=new SaveCoordinator(vault,TimeProvider.System))
            {
                var note=session.Workspace.Notes.Single();var original=note.Document;byte[] current=File.ReadAllBytes(Path.Combine(root,"current.vault"));
                bool inserted=await session.InsertInlineImageAsync(note,id,1,note.EditVersion);
                VaultChecks.Require(!inserted,"Unanchored real previous recovery candidate must refuse image publication without starting root writes");
                VaultChecks.Require(note.Document==original&&current.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Refused previous-candidate insertion preserves source and exact current ciphertext");
                VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Separate explicit recovery root anchoring succeeds");
                var field=typeof(SaveCoordinator).GetField("rootTask",BindingFlags.Instance|BindingFlags.NonPublic)!;var baseline=(Task<bool>)field.GetValue(session)!;var pending=new TaskCompletionSource<bool>();field.SetValue(session,pending.Task);Task<bool>? admission=null;
                try
                {
                    admission=session.InsertInlineImageAsync(note,id,1,note.EditVersion);VaultChecks.Require(admission.IsCompleted&&!await admission,"Pending root preparation rejects synchronously without image source publication");VaultChecks.Require(note.Document==original,"Pending root refusal leaves full source exact");
                }
                finally{field.SetValue(session,baseline);pending.TrySetResult(false);if(admission is not null)await admission;}
                var accepted=session.InsertInlineImageAsync(note,id,1,note.EditVersion);VaultChecks.Require(accepted.IsCompleted&&await accepted&&note.Document!.SchemaVersion==2,"Already anchored settled insertion commits synchronously with completed Task result");
                VaultChecks.Require(await session.SaveAsync()&&await session.LockAsync(),"Admission accepted image settles normally");
            }
            Console.WriteLine("PASS: H01 real previous-candidate/pending-root refusal, exact source/current preservation and anchored synchronous publication");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

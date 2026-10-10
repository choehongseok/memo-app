using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class BackupPreviewChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-backup-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var vault=EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret);using var active=new SaveCoordinator(vault,TimeProvider.System);var note=active.Workspace.CreateNote();note.Title="합성 백업";note.Text="😀"+new string('가',600);var cluster=active.Workspace.CreateNote();cluster.Title="grapheme";cluster.Text="a"+new string('\u0301',300)+"tail";Require(await active.PrepareAttachmentsAsync(),"Preview actual root");active.AttachBytes(note,new byte[]{0,255,3},"synthetic.bin","application/octet-stream",note.EditVersion);Require(await active.SaveAsync(),"Preview baseline saved");string backup=Path.Combine(root,"copy.vault");Require(await active.BackupAsync(backup),"Preview genuine backup");
            var method=typeof(SaveCoordinator).GetMethod("PreviewEncryptedBackup")??throw new Exception("Authenticated read-only backup preview is missing");var cipher=MemoApp.Core.Transfer.BackupFileReader.Read(backup);try{MemoApp.Core.Transfer.BackupFileReader.Read("https://example.invalid/backup.vault");throw new Exception("Remote backup read accepted");}catch(Exception e)when(e is IOException or ArgumentException){}var before=SnapshotSerialization.Bytes(active.Workspace.Capture());byte[] current=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));object preview=method.Invoke(active,[cipher])!;var notes=(Array)preview.GetType().GetProperty("Notes")!.GetValue(preview)!;object first=notes.GetValue(0)!;string excerpt=(string)first.GetType().GetProperty("Excerpt")!.GetValue(first)!;
            Require(notes.Length==2&&excerpt.StartsWith("😀",StringComparison.Ordinal)&&excerpt.Length<=256,"Detached bounded Unicode preview of authenticated original");Require((string)notes.GetValue(1)!.GetType().GetProperty("Excerpt")!.GetValue(notes.GetValue(1)!)! == "…","Single oversized combining cluster never bypasses 256 UTF16 budget");Require(before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&current.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault")))&&cipher.SequenceEqual(File.ReadAllBytes(backup)),"Preview leaves exact workspace/current/source unchanged");
            void Refuse(byte[] data){try{method.Invoke(active,[data]);throw new Exception("Invalid preview accepted");}catch(TargetInvocationException error)when(error.InnerException is InvalidDataException or CryptographicException or InvalidOperationException or ArgumentException){}}
            var tampered=(byte[])cipher.Clone();tampered[^1]^=1;Refuse(tampered);Refuse(cipher[..^1]);Refuse(new byte[16*1024*1024+1]);
            using(var other=EncryptedVault.Create(Path.Combine(root,"other"),secret,secret)){other.Save(new(4,Guid.NewGuid(),[]));Refuse(File.ReadAllBytes(Path.Combine(root,"other","current.vault")));}
            byte[] wrong=EncryptedVault.GenerateRecoverySecret();try{using var different=EncryptedVault.Create(Path.Combine(root,"wrong"),wrong,wrong);different.Save(new(4,Guid.NewGuid(),[]));Refuse(File.ReadAllBytes(Path.Combine(root,"wrong","current.vault")));}finally{CryptographicOperations.ZeroMemory(wrong);}
            Require(!vault.IsFaulted&&before.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&current.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault"))),"Read failure never faults or writes live vault");await active.LockAsync();Refuse(cipher);
            Console.WriteLine("PASS: authenticated same-vault bounded backup preview and exact state/source/fault invariance on refusal");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private static void Require(bool value,string message)=>VaultChecks.Require(value,message);
}

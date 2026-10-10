using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class SelectedPngChecks
{
    internal static readonly byte[] Png=Convert.FromHexString("89504e470d0a1a0a0000000d4948445200000002000000010806000000f4227f8a0000000e49444154789c63f8cfc000420d000f7a037e77e97f970000000049454e44ae426082");
    internal static async Task Run()
    {
        var type=typeof(EncryptedVault).Assembly.GetType("MemoApp.Core.Transfer.AttachmentPngPreview");
        VaultChecks.Require(type is not null,"Single-use authenticated PNG preview bridge is missing");
        var decode=type!.GetMethod("Decode")!.CreateDelegate<Func<AttachmentReadLease,CancellationToken,OwnedBgraRaster>>();
        string root=Path.Combine(Path.GetTempPath(),"memo-selected-png-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=owner.Workspace.CreateNote();VaultChecks.Require(await owner.PrepareAttachmentsAsync(),"PNG bridge root");
            var epochProperty=typeof(SaveCoordinator).GetProperty("AttachmentPreviewEpoch");VaultChecks.Require(epochProperty is not null,"Preview publication invalidation epoch missing");
            long Epoch()=>(long)epochProperty!.GetValue(owner)!;
            var valid=typeof(SaveCoordinator).GetMethod("IsAttachmentPreviewCurrent")!;
            Guid id=owner.AttachBytes(note,Png,"spoof.txt","application/octet-stream",note.EditVersion);VaultChecks.Require(await owner.SaveAsync(),"PNG bridge baseline");
            long epoch=Epoch();VaultChecks.Require((bool)valid.Invoke(owner,[note,id,note.EditVersion,epoch])!,"Fresh selected source authority");
            using(var lease=owner.CreateAttachmentReadLease(note,id,note.EditVersion))using(var raster=decode(lease,default))
            {
                VaultChecks.Require(raster.Width==2&&raster.Height==1&&raster.Stride==8&&owner.WhenAttachmentReadsIdle.IsCompleted,"Detached PNG dimensions and source grant drained");
                VaultChecks.Require(raster.ConsumePixels(p=>VaultChecks.Require(p.SequenceEqual(new byte[]{0,0,255,255,255,0,0,128}),"Independent literal RGB/alpha oracle")),"Single borrowed raster consumed");
                VaultChecks.ExpectFailure(()=>decode(lease,default).Dispose(),"Bridge refuses consumed source");
            }
            var snapshot=owner.Workspace.Capture();owner.Workspace.AcceptPrepared(snapshot);VaultChecks.Require(Epoch()!=epoch&&!(bool)valid.Invoke(owner,[note,id,note.EditVersion,epoch])!,"Unchanged-version AcceptPrepared invalidates detached publication authority");
            using(var lease=owner.CreateAttachmentReadLease(note,id,note.EditVersion)){bool canceled=false;try{decode(lease,new CancellationToken(true)).Dispose();}catch(OperationCanceledException){canceled=true;}VaultChecks.Require(canceled,"Canceled bridge refuses transfer");VaultChecks.Require(owner.WhenAttachmentReadsIdle.IsCompleted,"Canceled bridge drains owned source");}
            using(var lease=owner.CreateAttachmentReadLease(note,id,note.EditVersion)){note.Title="invalidate";VaultChecks.ExpectFailure(()=>decode(lease,default).Dispose(),"Revoked source cannot decode");}
            VaultChecks.Require(await owner.SaveAsync(),"Bridge mutation saved");await owner.LockAsync();
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

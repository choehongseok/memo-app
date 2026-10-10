using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
internal static class AttachmentPreparationChecks
{
    internal static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-attachment-prepare-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            using var vault=EncryptedVault.Create(root,secret,secret);using var active=new SaveCoordinator(vault,TimeProvider.System);active.Workspace.CreateNote().Text="unanchored synthetic";
            var candidate=(VaultSnapshot)typeof(EncryptedVault).GetMethod("InitializeAttachmentRoot",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vault,[active.Workspace.Capture()])!;active.Workspace.AcceptPrepared(candidate);long before=active.AttachmentPreviewEpoch;
            VaultChecks.Require(candidate.AttachmentRootId!=Guid.Empty,"Unanchored nonempty root fixture");var preparation=active.PrepareAttachments(before);VaultChecks.Require(preparation.PreviewEpoch==before+1&&active.AttachmentPreviewEpoch==preparation.PreviewEpoch&&await preparation.Completion,"Nonempty unanchored root still predicts legitimate synchronous acceptance");
            before=active.AttachmentPreviewEpoch;preparation=active.PrepareAttachments(before);VaultChecks.Require(preparation.PreviewEpoch==before&&await preparation.Completion&&active.AttachmentPreviewEpoch==before,"Already anchored root makes no acceptance transition");
            VaultChecks.ExpectFailure(()=>active.PrepareAttachments(before-1),"Stale preparation input is refused");await active.LockAsync();VaultChecks.ExpectFailure(()=>active.PrepareAttachments(active.AttachmentPreviewEpoch),"Locked preparation input refused");
            Console.WriteLine("PASS: authoritative anchored/unanchored preparation epochs and stale/locked refusal");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

using MemoApp.Core.Documents;
using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class SaveCoordinator
{
    public Task<bool> InsertInlineImageAsync(NoteDraft note,Guid attachmentId,int blockBoundary,long expectedEditVersion)
    {
        long epoch=sessionEpoch;RequireAttachmentSource(note,expectedEditVersion,epoch);
        if(IsBusy||!rootTask.IsCompleted||!vault.AttachmentRootAnchored)return Task.FromResult(false);
        var workspace=Workspace;var original=note.Document;
        if(note.Mode!="rich"||original is null)throw new InvalidOperationException("Rich source required");
        var item=workspace.AttachmentObject(note,attachmentId);if(item.Mime!="image/png")throw new InvalidOperationException("Authenticated PNG attachment required");
        _=RichDocumentImageEdit.Insert(original,blockBoundary,attachmentId,item.Name);
        RequireAttachmentSource(note,expectedEditVersion,epoch);
        if(!ReferenceEquals(workspace,Workspace)||note.Document!=original||!AttachmentValidation.SameObject(item,Workspace.AttachmentObject(note,attachmentId)))throw new InvalidOperationException("Inline image source authority changed");
        Workspace.InsertInlineImage(note,attachmentId,blockBoundary,item.Name);return Task.FromResult(true);
    }
    public void RemoveInlineImage(NoteDraft note,int blockIndex,long expectedEditVersion)
    {
        RequireAttachmentSource(note,expectedEditVersion,sessionEpoch);Workspace.RemoveInlineImage(note,blockIndex);
    }
}

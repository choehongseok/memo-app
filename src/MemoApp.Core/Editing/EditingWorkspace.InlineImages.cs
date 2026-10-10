using MemoApp.Core.Documents;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    internal void InsertInlineImage(NoteDraft note,Guid attachmentId,int blockBoundary,string alt)
    {
        RequireNote(note);if(note.Mode!="rich"||note.Document is null)throw new InvalidOperationException("Rich source required");
        var item=AttachmentObject(note,attachmentId);if(item.Mime!="image/png")throw new InvalidOperationException("Authenticated PNG attachment required");
        var document=RichDocumentImageEdit.Insert(note.Document,blockBoundary,attachmentId,alt);ApplyImageDocument(note,document);
    }
    internal void RemoveInlineImage(NoteDraft note,int blockIndex)
    {
        RequireNote(note);if(note.Mode!="rich"||note.Document is null)throw new InvalidOperationException("Rich source required");
        ApplyImageDocument(note,RichDocumentImageEdit.Remove(note.Document,blockIndex));
    }
    private void ApplyImageDocument(NoteDraft note,StyledDocument document)
    {
        string text=RichDocumentCodec.Inspect(document).Text!;
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,Storage.NoteMetadata Metadata)>{[note.Id]=(note.Title,text,note.Metadata)},
            new Dictionary<Guid,(string Mode,StyledDocument? Document)>{[note.Id]=("rich",document)},activateInlineImages:true);
    }
}

using MemoApp.Core.Storage;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    public void AddFilePathLink(NoteDraft note,StoredFilePathLink link)
    {
        RequireNote(note);FilePathLinkValidation.Link(link);
        if(attachmentRootId==Guid.Empty)throw new InvalidOperationException("Encrypted root must be prepared before schema7 activation");
        var metadata=note.Metadata with{FilePathLinks=note.Metadata.FilePathLinks.Add(link)};
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,note.Text,metadata)},activateFilePaths:true);
    }
    public void DetachFilePathLink(NoteDraft note,Guid linkId)
    {
        RequireNote(note);var link=note.Metadata.FilePathLinks.SingleOrDefault(l=>l.Id==linkId)??throw new ArgumentException("Unknown file path link");
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,note.Text,note.Metadata with{FilePathLinks=note.Metadata.FilePathLinks.Remove(link)})});
    }
}

using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Editing;
public sealed partial class EditingWorkspace
{
    public NoteDraft[] ImportTexts(IEnumerable<ImportedText> input,Guid? folderId=null)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(input);var incoming=input.Take(101).ToArray();if(incoming.Length is <1 or >100||incoming.Any(n=>n is null))throw new ArgumentException("Imported batch count");var before=Capture();var now=clock.GetUtcNow();int order=NextOrder();
        var sources=incoming.Select((n,i)=>new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,n.Title,n.Text){Metadata=new(){FolderId=folderId,Order=Math.Min(1000000,order+i)}}).ToArray();var candidate=before with{Notes=before.Notes.Concat(sources).ToArray()};VaultEnvelope.Validate(candidate);
        var added=sources.Select(n=>new NoteDraft(clock,n)).ToArray();foreach(var draft in added)AddDraft(draft);AcceptPrepared(candidate);Changed?.Invoke();foreach(var draft in added){if(closed)break;notes.PublishAdded(draft);}return added;
    }
}

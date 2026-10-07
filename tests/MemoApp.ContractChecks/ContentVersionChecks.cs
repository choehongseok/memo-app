using MemoApp.Core.Editing;
using MemoApp.Core.Documents;
internal static class ContentVersionChecks
{
    internal static void Run()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();
        long Version()=>(long)(typeof(NoteDraft).GetProperty("ContentVersion")?.GetValue(note)??throw new Exception("Missing content-specific version boundary"));
        long initial=Version();note.Title="metadata title";note.Favorite=true;VaultChecks.Require(Version()==initial&&note.EditVersion>0,"title/metadata cannot invalidate local content completion");note.Text="content";VaultChecks.Require(Version()>initial,"plain text advances content version");workspace.ConvertMode(note,"rich",true);long rich=Version();var original=note.Document;workspace.AcceptPrepared(workspace.Capture());note.Title="later title";workspace.SetTags(note,["tag"]);VaultChecks.Require(Version()==rich&&note.Document==original,"metadata event staging preserves content authority");
        var revision=workspace.HistoryFor(note).First(r=>r.Mode=="rich").RevisionId;workspace.RestoreRevision(note,revision);VaultChecks.Require(note.Document==original&&Version()>rich,"explicit same-content revision restore invalidates pending local content even when document equality is unchanged");long restored=Version();workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("new content"));VaultChecks.Require(Version()>restored,"rich typing advances content authority");long last=Version();try{workspace.SetRichDocument(note,new(1,"{\"nodes\":[{\"type\":\"unknown\"}]}"));throw new Exception("unsupported content accepted");}catch(InvalidOperationException){}VaultChecks.Require(Version()==last,"rejected content does not advance content authority");workspace.Clear();VaultChecks.Require(Version()>last&&note.IsClosed,"closed draft revokes pending content authority");Console.WriteLine("PASS: content-only version separates metadata from native completion and invalidates explicit same-content restoration/close without advancing on refusal");
    }
}

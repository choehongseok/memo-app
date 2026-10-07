using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
internal static class AttachmentMetadataChecks
{
    internal static void Run()
    {
        var describe=typeof(EditingWorkspace).GetMethod("DescribeAttachments",BindingFlags.Public|BindingFlags.Instance);
        VaultChecks.Require(describe is not null,"Safe attachment metadata-only projection is missing");
        VaultChecks.Require(Enum.TryParse<SearchField>("Attachments",out var field),"Attachment metadata search field is missing");
        var key=RandomNumberGenerator.GetBytes(32);var bytes=RandomNumberGenerator.GetBytes(1024);Guid root=Guid.NewGuid(),vault=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        try
        {
            var item=AttachmentObjectCodec.Encrypt(bytes,vault,root,key,"합성 Cafe\u0301😀.PDF","application/pdf");
            var stored=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"ordinary title","ordinary body"){AttachmentIds=[item.ObjectId]};
            var basis=new VaultSnapshot(5,Guid.NewGuid(),[stored,new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"other","no source")]){AttachmentRootId=root,AttachmentObjects=[item]};
            var workspace=new EditingWorkspace(TimeProvider.System,basis);var note=workspace.Notes[0];var values=((IEnumerable)describe!.Invoke(workspace,[note])!).Cast<object>().ToArray();VaultChecks.Require(values.Length==1,"Projection contains active immutable reference only");var value=values[0];object Read(string name)=>value.GetType().GetProperty(name)!.GetValue(value)!;
            VaultChecks.Require((Guid)Read("Id")==item.ObjectId&&(string)Read("Name")==item.Name&&(string)Read("Mime")==item.Mime&&(int)Read("Length")==item.Length&&(string)Read("Sha256")==item.Sha256,"Projection preserves safe exact ID/name/MIME/length/hash");
            VaultChecks.Require(value.GetType().GetProperties().Select(p=>p.Name).Order().SequenceEqual(new[]{"Id","Length","Mime","Name","Sha256"}.Order()),"Projection cannot disclose ciphertext/chunks/root/key/path/plaintext content");
            VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=field,Query="Café😀.pdf"}).SequenceEqual([note]),"Metadata filename search uses NFC and ordinal ignore-case");
            VaultChecks.Require(NoteSearch.Find(workspace,new(){Query=".PDF"}).SequenceEqual([note]),"All-fields includes inert attachment names/extensions");
            VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=SearchField.Body,Query=".pdf"}).Length==0&&NoteSearch.Find(workspace,new(){Field=SearchField.Title,Query=".pdf"}).Length==0,"Title/body-only fields keep their existing scope");
            VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=field,Query="application/pdf"}).Length==0,"Search does not mistake MIME or binary content for original filename");
            workspace.DeleteNote(note);VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=field,Query=".pdf"}).Length==0&&NoteSearch.Find(workspace,new(){Field=field,Query=".pdf",View=SearchView.Trash}).SequenceEqual([note]),"Attachment metadata honors active/trash view without decryption");workspace.RestoreNote(note);
            typeof(EditingWorkspace).GetMethod("DetachAttachment",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(workspace,[note,item.ObjectId]);VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=field,Query=".pdf"}).Length==0&&((IEnumerable)describe.Invoke(workspace,[note])!).Cast<object>().Count()==0,"Detached historical ciphertext is not a current-note filename search hit");
            workspace.Clear();VaultChecks.ExpectFailure(()=>workspace.DescribeAttachments(note),"Closed workspace cannot issue descriptions");VaultChecks.Require(NoteSearch.Find(workspace,new(){Field=field,Query=".pdf"}).Length==0,"Closed search does not retain attachment names");
            Console.WriteLine("PASS: metadata-only attachment descriptions, exact safe fields, NFC/literal/case name-extension search and active/trash/detach/lock scope (no binary parsing)");
        }
        finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(bytes);}
    }
}

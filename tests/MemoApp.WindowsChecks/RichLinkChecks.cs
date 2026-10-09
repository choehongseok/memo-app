using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task RichLinkRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-rich-link-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();Window? host=null;StructuredNoteEditor? view=null;
        try
        {
            using var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();note.Text="synthetic link";session.Workspace.ConvertMode(note,"rich",true);view=new(session.Workspace,note,()=>!session.IsLocked,_=>{});host=new Window{Content=view};host.Show();await Idle();var paragraph=(Paragraph)view.RichInput.Document.Blocks.FirstBlock;view.RichInput.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);view.ApplyLink("https://example.invalid/synthetic?q=%22literal%22");await Idle();Require(await session.SaveAsync(),"Link baseline actual encrypted save");
            paragraph=(Paragraph)view.RichInput.Document.Blocks.FirstBlock;var run=paragraph.Inlines.OfType<Run>().FirstOrDefault()??paragraph.Inlines.OfType<Span>().First().Inlines.OfType<Run>().First();view.RichInput.Selection.Select(run.ContentStart,run.ContentStart);string original=note.Document!.SourceJson;long version=note.EditVersion;byte[] encrypted=File.ReadAllBytes(Path.Combine(root,"current.vault"));int launched=0;string? confirmed=null,opened=null;
            Require(!view.OpenSelectedLink(_=>false,_=>launched++)&&launched==0,"Cancelled explicit link never launches");Require(view.OpenSelectedLink(url=>{confirmed=url;return true;},url=>{opened=url;launched++;})&&launched==1&&confirmed==opened&&opened=="https://example.invalid/synthetic?q=%22literal%22","Same validated target confirmation/launch; injected browser has no actual network");Require(note.Document.SourceJson==original&&note.EditVersion==version&&!session.IsDirty&&encrypted.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Confirmed open preserves source/history/dirty/ciphertext");
            Require(!view.OpenSelectedLink(_=>{note.Title="changed in confirmation";return true;},_=>launched++)&&launched==1,"Source version change in modal confirmation refuses launch");await session.SaveAsync();
            Require(!view.OpenSelectedLink(_=>throw new IOException("Synthetic confirmation fault"),_=>launched++)&&launched==1,"Confirmation exception refuses launch without source mutation");
            Task<bool>? locking=null;Require(!view.OpenSelectedLink(_=>{locking=session.LockAsync();return true;},_=>launched++)&&launched==1&&session.IsLocked,"Modal lock refuses stale browser request");if(locking is not null)await locking;Require(session.KeysReleased&&note.IsClosed,"Actual lock releases keys and closes link source");
        }
        finally{view?.Dispose();host?.Close();CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

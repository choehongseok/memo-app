using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task MarkdownImportRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-markdown-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            string root=Path.Combine(dir,"vault"),path=Path.Combine(dir,"합성 Obsidian.md"),raw="---\r\naliases: [합성]\r\n---\n# **원문**\r\n[[메모]] ![[image.png]]\r<script>literal</script>\n";byte[] original=new UTF8Encoding(false,true).GetBytes(raw);File.WriteAllBytes(path,original);
            main=new MainWindow(root);main.Show();Require(main.FindName("MarkdownImportButton") is Button,"Explicit raw Markdown file import control is missing");Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Text="current preserved";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");Require(await active.SaveAsync(),"Markdown native baseline");
            var method=typeof(MainWindow).GetMethod("ImportMarkdownAsync",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Source guarded Markdown import command missing");Task<bool> Import(Func<string?> choose,Func<bool> confirm)=>(Task<bool>)method.Invoke(main,[choose,confirm])!;
            Require(!await Import(()=>null,()=>throw new Exception("No confirm on cancel"))&&active.Workspace.Notes.Count==1,"Canceled Markdown picker leaves original only");
            Require(!await Import(()=>{note.Title="newer";return path;},()=>throw new Exception("No confirm after picker edit"))&&active.Workspace.Notes.Count==1,"Picker edit revokes before read/import");Require(await active.SaveAsync(),"Picker edit saved");
            Require(!await Import(()=>path,()=>false)&&active.Workspace.Notes.Count==1,"Limitations confirmation cancellation preserves current");
            Require(!await Import(()=>path,()=>{note.Title="confirm source edit";return true;})&&active.Workspace.Notes.Count==1,"Confirmation edit rejects stale source");Require(await active.SaveAsync(),"Confirm edit saved");
            Require(await Import(()=>path,()=>true)&&active.Workspace.Notes.Count==2&&active.Workspace.Notes.Single(n=>n.Id!=note.Id).Mode=="markdown"&&active.Workspace.Notes.Single(n=>n.Id!=note.Id).Text==raw&&note.Text=="current preserved"&&File.ReadAllBytes(path).SequenceEqual(original),"Actual native Markdown import preserves raw newlines/extensions and existing/current/source bytes");await Field<Task>(main,"recentTask");
            byte[] accepted=File.ReadAllBytes(Path.Combine(root,"current.vault"));Action failObserver=()=>throw new IOException("Synthetic import observer failure");active.Workspace.Changed+=failObserver;try{Require(!await Import(()=>path,()=>true)&&active.Workspace.Notes.Count==3&&active.IsDirty&&Control<TextBlock>(main,"Notice").Text.Contains("추가 이후")&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(accepted),"Observer failure after applied fresh note reports applied state and retains entire raw document/dirty original ciphertext");}finally{active.Workspace.Changed-=failObserver;}Require(await active.SaveAsync(),"Observer failure explicit save repaired");
            Task<bool>? locking=null;Require(!await Import(()=>path,()=>{locking=active.LockAsync();return true;}),"Confirmation lock discards late imported strings before apply");await locking!;Require(!Control<Button>(main,"MarkdownImportButton").IsEnabled&&active.KeysReleased,"Lock disables Markdown import and ends keys");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);}
    }
}

using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task AttachmentOpenRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-attachment-open-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();Window? host=null;
        try
        {
            using var active=new SaveCoordinator(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();Require(await active.PrepareAttachmentsAsync(),"Open actual root");byte[] body=[0,1,2,255,4];var id=active.AttachBytes(note,body,"synthetic.bin","application/octet-stream",note.EditVersion);Require(await active.SaveAsync(),"Open saved source");string message="";using var panel=new AttachmentPanel(active,note,()=>true,m=>message=m);host=new Window{Content=panel};host.Show();panel.FilesList.SelectedIndex=0;
            Require(typeof(AttachmentPanel).GetProperty("OpenButton")?.GetValue(panel) is Button,"Explicit attachment open control is missing");var method=typeof(AttachmentPanel).GetMethod("OpenSelectedAsync")??throw new Exception("Attachment source protected open command is missing");int launches=0;
            Task<bool> Open(string name,Func<bool>? confirm=null,Func<string?>? choose=null,Action<string>? launch=null,IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(panel,[confirm??(()=>true),choose??(()=>Path.Combine(dir,name)),launch??(p=>{Require(Path.IsPathFullyQualified(p)&&File.ReadAllBytes(p).SequenceEqual(body),"Launch exact authenticated local file");launches++;}),files])!;
            Require(!await Open("cancel.bin",()=>false)&&launches==0&&!File.Exists(Path.Combine(dir,"cancel.bin")),"Cancel before plaintext file or launch");
            Require(!await Open("edited.bin",()=>{note.Title="new source";return true;})&&!File.Exists(Path.Combine(dir,"edited.bin"))&&launches==0,"Confirmation edit stops before picker/copy/launch");Require(await active.SaveAsync(),"Open edit accepted");panel.FilesList.SelectedIndex=0;
            Require(!await Open("selection.bin",choose:()=>{panel.FilesList.SelectedIndex=-1;return Path.Combine(dir,"selection.bin");})&&!File.Exists(Path.Combine(dir,"selection.bin")),"Picker selection change prevents copy");panel.FilesList.SelectedIndex=0;
            Task<bool>? duplicate=null;Require(!await Open("duplicate.bin",()=>{duplicate=Open("duplicate2.bin",()=>throw new Exception("No duplicate modal"));return false;})&&duplicate is{IsCompletedSuccessfully:true}&&!duplicate.Result,"Double-click busy admission before modal");
            Require(!await Open("inside.bin",choose:()=>Path.Combine(root,"unsafe.bin"))&&!File.Exists(Path.Combine(root,"unsafe.bin"))&&launches==0,"Active vault plaintext refused");
            Require(await Open("good.bin")&&launches==1&&message.Contains("평문"),"Successful explicit copy and one external initiation with remaining plaintext notice");
            Require(!await Open("good.bin")&&launches==1&&File.ReadAllBytes(Path.Combine(dir,"good.bin")).SequenceEqual(body),"Existing destination never replaced or launched again");
            Require(!await Open("launch-fail.bin",launch:_=>throw new IOException("Synthetic launch failure"))&&File.ReadAllBytes(Path.Combine(dir,"launch-fail.bin")).SequenceEqual(body)&&message.Contains("평문"),"Launch failure truthfully retains plaintext copy");
            Task<bool>? locking=null;var flush=new FlushCallbackFiles(()=>panel.Dispatcher.Invoke(()=>{locking=active.LockAsync();}));Require(!await Open("lock-after-write.bin",files:flush)&&launches==1,"Lock after actual write before launch discards late launch");await locking!;Require(active.KeysReleased&&File.ReadAllBytes(Path.Combine(dir,"lock-after-write.bin")).SequenceEqual(body),"Key release does not promise deletion of external plaintext copy");
        }
        finally{host?.Close();CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    private sealed class FlushCallbackFiles(Action callback):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){actual.FlushToDisk(stream);callback();}public void Move(string a,string b)=>throw new NotSupportedException();public void Replace(string a,string b,string c)=>throw new NotSupportedException();
    }
}

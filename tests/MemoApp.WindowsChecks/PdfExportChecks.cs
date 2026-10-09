using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task PdfExportRun()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-pdf-export-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");Directory.CreateDirectory(dir);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Require(main.FindName("PdfExportButton") is Button,"Explicit PDF native export control");Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="합성 PDF";note.Text="한글 漢字 ABC /JavaScript";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");Require(await active.SaveAsync(),"PDF native baseline saved");await Idle();
            var method=typeof(MainWindow).GetMethod("ExportSelectedPdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;Task<bool> Export(string name,Func<bool>? confirm=null,Func<string?>? choose=null,IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(main,[confirm??(()=>true),choose??(()=>Path.Combine(dir,name)),files])!;
            var closures=typeof(MainWindow).GetNestedTypes(BindingFlags.NonPublic).Where(t=>t.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(m=>m.Name.StartsWith("<WritePdfExportAsync>",StringComparison.Ordinal))).ToArray();var allowed=new[]{typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};Require(closures.Length==1&&closures[0].GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).All(f=>allowed.Contains(f.FieldType)),"Compiled PDF write worker excludes window/draft/session/key owners");
            var enabled=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.UIElement.IsEnabledProperty,typeof(Button));bool once=true;EventHandler disabling=(_,_)=>{if(once&&!Control<Button>(main,"PdfExportButton").IsEnabled){once=false;throw new IOException("Synthetic PDF native disable failure");}};enabled.AddValueChanged(Control<Button>(main,"PdfExportButton"),disabling);try{Require(!await Export("disable.pdf",()=>throw new Exception("No PDF consent after native failure"))&&!Field<bool>(main,"pdfExportBusy")&&!File.Exists(Path.Combine(dir,"disable.pdf")),"Native PDF disable failure releases admission without file");}finally{enabled.RemoveValueChanged(Control<Button>(main,"PdfExportButton"),disabling);}
            Require(!await Export("cancel.pdf",()=>false,()=>throw new Exception("No PDF picker without consent")),"Default-No confirmation cancellation");Require(!await Export("picker-cancel.pdf",choose:()=>null),"Picker cancellation");
            Require(!await Export("edited.pdf",()=>{note.Title="new source";return true;},()=>throw new Exception("No stale PDF picker")),"Consent edit revokes before picker");Require(await active.SaveAsync(),"Consent edit saved");
            Require(!await Export("same.pdf",choose:()=>{active.Workspace.AcceptPrepared(active.Workspace.Capture());return Path.Combine(dir,"same.pdf");})&&!File.Exists(Path.Combine(dir,"same.pdf")),"Same-version source acceptance refuses PDF construction/write");
            Require(!await Export("inside.pdf",choose:()=>Path.Combine(root,"inside.pdf"))&&!File.Exists(Path.Combine(root,"inside.pdf")),"Plaintext PDF inside vault refused");Require(!await Export("wrong.txt")&&!File.Exists(Path.Combine(dir,"wrong.txt")),"PDF extension required");
            note.Text="unsupported 😀";Require(!await Export("missing-glyph.pdf")&&!File.Exists(Path.Combine(dir,"missing-glyph.pdf"))&&note.Text.Contains("😀"),"Unsupported original preserved and no file created");note.Text="한글 원문 漢字 ABC /JavaScript";Require(await active.SaveAsync(),"Verified source settled");await Idle();byte[] snapshot=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));Require(await Export("synthetic.pdf"),"Actual native plaintext PDF export");byte[] pdf=File.ReadAllBytes(Path.Combine(dir,"synthetic.pdf"));Require(pdf.AsSpan().StartsWith("%PDF-1.7"u8)&&pdf.Length<16*1024*1024&&snapshot.SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Valid PDF output and exact full source/ciphertext invariance");Require(!await Export("synthetic.pdf")&&pdf.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"synthetic.pdf"))),"Existing PDF not overwritten");
            string fresh=Path.Combine(dir,"late.pdf");var paused=new PausedBatchFiles();var pending=Export("late.pdf",files:paused);await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted,"Lock releases keys while PDF worker holds only prepared bytes");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(fresh).Length==0,"Late CreateNew cancellation writes no PDF plaintext");await locking;Require(!Control<Button>(main,"PdfExportButton").IsEnabled,"Locked PDF command disabled");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);}
    }
}

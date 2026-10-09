using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task OfficeExportRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-office-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("ExcelExportButton") is Button&&main.FindName("WordExportButton") is Button,"Offline Excel and Word native export commands are missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="합성 제목";note.Text="=SUM(1,2) 한글 😀";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");var method=typeof(MainWindow).GetMethod("ExportSelectedOfficeAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var closures=typeof(MainWindow).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t=>t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Any(m=>m.Name.StartsWith("<WriteOfficeExportAsync>",StringComparison.Ordinal))).ToArray();var allowed=new[]{typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};Require(closures.Length==1&&closures[0].GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).All(f=>allowed.Contains(f.FieldType)),"Compiled Office worker excludes window/note/session/key owners");
            string denied=Path.Combine(root,"denied.xlsx");Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>{note.Text="changed in consent";return true;}),new Func<string?>(()=>throw new Exception("No stale source picker")),null])!&&!File.Exists(denied),"Consent source-version change refuses before picker");
            Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>true),new Func<string?>(()=>{active.Workspace.AcceptPrepared(active.Workspace.Capture());return denied;}),null])!&&!File.Exists(denied),"Same-version accepted-source epoch change refuses after picker");
            Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>true),new Func<string?>(()=>Path.Combine(root,"vault","denied.xlsx")),null])!&&!File.Exists(Path.Combine(root,"vault","denied.xlsx")),"Office plaintext output inside active vault refused");
            foreach(var format in new[]{OfficeTextFormat.Spreadsheet,OfficeTextFormat.Word})
            {
                string path=Path.Combine(root,format==OfficeTextFormat.Spreadsheet?"selected.xlsx":"selected.docx");
                Require(!await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>false),new Func<string?>(()=>throw new Exception("No Office picker without plaintext confirmation")),null])!&&!File.Exists(path),"Plaintext refusal produces no package");
                Require(await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>true),new Func<string?>(()=>path),null])!,"Actual selected native Office export writes package");using var zip=ZipFile.OpenRead(path);Require(zip.GetEntry("[Content_Types].xml") is not null,"Generated package has content types");
            }
            active.Workspace.ConvertMode(note,"rich",true);active.Workspace.SetRichDocument(note,new(1,"""{"nodes":[{"type":"paragraph","runs":[{"text":"서식 합성 😀","bold":true,"underline":true,"fontFamily":"D2Coding","fontSize":16,"foreground":"#123456","link":"https://example.invalid/synthetic"}]},{"type":"list","ordered":true,"items":[{"runs":[{"text":"순서"}]}]},{"type":"checklist","items":[{"checked":true,"runs":[{"text":"완료"}]}]},{"type":"table","rows":[[{"runs":[{"text":"셀"}]}]]}]}"""));Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string canonical=note.Document!.SourceJson;string richPath=Path.Combine(root,"rich-selected.docx");
            Require(await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,new Func<bool>(()=>true),new Func<string?>(()=>richPath),null])!,"Actual native Word command exports canonical rich source");
            using(var zip=ZipFile.OpenRead(richPath)){using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";Require(xml.Descendants(w+"b").Any()&&xml.Descendants(w+"u").Any()&&xml.Descendants(w+"numPr").Count()==1&&xml.Descendants(w+"tbl").Count()==1&&xml.Descendants(w+"t").Any(t=>t.Value=="[x] ")&&xml.Descendants(w+"t").Any(t=>t.Value.Contains("https://example.invalid/synthetic")),"Actual Word route preserves run styles/list/table/check state and inert URL");}Require(note.Document!.SourceJson==canonical,"Actual Word export preserves canonical original");
            string fresh=Path.Combine(root,"cancelled.docx");var paused=new PausedBatchFiles();var pending=(Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,new Func<bool>(()=>true),new Func<string?>(()=>fresh),paused])!;await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted,"Lock immediately releases keys while Office worker holds only prepared bytes");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(fresh).Length==0,"Late Office CreateNew cancellation writes no plaintext");await locking;Require(!Control<Button>(main,"ExcelExportButton").IsEnabled&&!Control<Button>(main,"WordExportButton").IsEnabled,"Locked Office commands disabled");
        }
        finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

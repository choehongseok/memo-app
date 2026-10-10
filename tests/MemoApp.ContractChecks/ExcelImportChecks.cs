using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class ExcelImportChecks
{
    internal static async Task Run()
    {
        ExcelImportFailureChecks.Run();
        await ExcelImportStorageChecks.Run();
        var type=typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.ExcelTextTransfer")??throw new Exception("Bounded offline Excel text import is missing");var read=type.GetMethod("Read")!;
        var source=new EditingWorkspace(TimeProvider.System);var note=source.CreateNote();note.Title="합성 _x0041_";note.Text=new string('a',32766)+"😀"+new string('b',32768);string root=Path.Combine(Path.GetTempPath(),"memo-xlsx-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"synthetic.xlsx");using(var prepared=OfficeTextTransfer.Capture([note],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,path);byte[] before=SHA256.HashData(File.ReadAllBytes(path));var result=read.Invoke(null,[path,CancellationToken.None])!;var notes=(ImportedText[])result.GetType().GetProperty("Notes")!.GetValue(result)!;VaultChecks.Require(notes.Single().Title==note.Title&&notes.Single().Text==note.Text&&before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Own XLSX exact text/surrogate/escape roundtrip and read-only source");
            var import=typeof(EditingWorkspace).GetMethod("ImportTexts")??throw new Exception("Atomic imported text batch is missing");var destination=new EditingWorkspace(TimeProvider.System);var old=destination.CreateNote();old.Text="existing dirty original";int changed=0,notifications=0;destination.Changed+=()=>changed++;((System.Collections.Specialized.INotifyCollectionChanged)destination.Notes).CollectionChanged+=(_,e)=>{if(e.Action!=System.Collections.Specialized.NotifyCollectionChangedAction.Add)return;notifications++;VaultChecks.Require(destination.Notes.Count==3,"Every added notification observes the whole imported batch");};var two=new[]{notes.Single(),new ImportedText("합성 둘","second","")};import.Invoke(destination,[two,null]);VaultChecks.Require(changed==1&&notifications==2&&destination.Notes.Count==3&&old.Text=="existing dirty original","One atomic candidate preserves existing dirty edits");string snapshot=JsonSerializer.Serialize(destination.Capture());try{import.Invoke(destination,[new[]{new ImportedText("valid","ok",""),new ImportedText("bad",new string('x',65537),"")},null]);throw new Exception("Invalid late row accepted");}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException or ArgumentException or IOException or InvalidDataException){}VaultChecks.Require(JsonSerializer.Serialize(destination.Capture())==snapshot,"Invalid final row applies no early rows");destination.Clear();
        }
        finally{source.Clear();Directory.Delete(root,true);}
    }
}

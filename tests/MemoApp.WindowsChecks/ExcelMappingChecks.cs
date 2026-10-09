using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task ExcelMappingRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-mapping-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;var fixture=new EditingWorkspace(TimeProvider.System);
        try
        {
            var note=fixture.CreateNote();note.Title="first original";note.Text="body";string input=Path.Combine(root,"mapping.xlsx");using(var prepared=OfficeTextTransfer.Capture([note],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,input);
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";using(var zip=ZipFile.Open(input,ZipArchiveMode.Update))
            {
                void Change(string path,Action<XDocument> action){var entry=zip.GetEntry(path)!;XDocument xml;using(var stream=entry.Open())xml=XDocument.Load(stream);entry.Delete();action(xml);using var output=zip.CreateEntry(path).Open();xml.Save(output);}
                Change("xl/workbook.xml",xml=>xml.Root!.Element(ns+"sheets")!.Add(new XElement(ns+"sheet",new XAttribute("name","actual second"),new XAttribute("sheetId","2"),new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships")+"id","second"))));
                Change("xl/_rels/workbook.xml.rels",xml=>xml.Root!.Add(new XElement(xml.Root.Name.Namespace+"Relationship",new XAttribute("Id","second"),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),new XAttribute("Target","worksheets/second.xml"))));
                using var output=zip.CreateEntry("xl/worksheets/second.xml").Open();new XDocument(new XElement(ns+"worksheet",new XElement(ns+"sheetData",new XElement(ns+"row",new XAttribute("r","1"),new XElement(ns+"c",new XAttribute("r","C1"),new XAttribute("t","n"),new XElement(ns+"v","9007199254740993")),new XElement(ns+"c",new XAttribute("r","F1"),new XAttribute("t","b"),new XElement(ns+"v","1")))))).Save(output);
            }
            byte[] original=File.ReadAllBytes(input);var catalog=ExcelTextTransfer.Inspect(input);
            var dialog=new ExcelImportSelectionDialog(catalog);dialog.Show();Require(Field<RadioButton>(dialog,"mapping").IsChecked==true&&Field<CheckBox>(dialog,"skip").IsChecked==true&&Field<TextBox>(dialog,"title").Text=="1"&&Field<TextBox>(dialog,"body").Text=="2","Actual picker defaults to explicit mapping, columns 1/2 and visible skip-first-row choice");Field<ComboBox>(dialog,"sheet").SelectedIndex=1;Field<TextBox>(dialog,"title").Text="3";Field<TextBox>(dialog,"body").Text="6";Field<CheckBox>(dialog,"skip").IsChecked=false;
            Require((bool)typeof(ExcelImportSelectionDialog).GetMethod("TrySelect",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,[])!&&dialog.Selection is{SheetIndex:2,TitleColumn:3,BodyColumn:6,SkipFirstRow:false},"Actual controls produce non-first mapped DTO");dialog.Revoke();Require(dialog.Selection is null&&dialog.Content is null&&!dialog.IsVisible,"Picker revocation clears visible metadata and result");
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            WorkbookImportSelection Select(WorkbookImportCatalog c)=>new(2,3,6,false,c.SourceSha256);
            Task<bool> Import(Func<WorkbookImportCatalog,WorkbookImportSelection?> select,Func<int,bool>? confirm=null)=>main.ImportMappedExcelAsync(()=>input,select,confirm??(_=>true));
            Require(!await Import(_=>null)&&active.Workspace.Notes.Count==0,"Cancelled mapping never confirms or adds");
            Require(await Import(Select,count=>count==1)&&active.Workspace.Notes.Single().Title=="9007199254740993"&&active.Workspace.Notes.Single().Text=="TRUE","Actual non-first column mapping applies single plain candidate");string reopenRoot=Path.Combine(root,"reopen");Directory.CreateDirectory(reopenRoot);foreach(string file in Directory.EnumerateFiles(Path.Combine(root,"vault"),"*.vault"))File.Copy(file,Path.Combine(reopenRoot,Path.GetFileName(file)));using(var reopened=EncryptedVault.Open(reopenRoot,secret))Require(reopened.Loaded.Notes.Single().Title=="9007199254740993"&&reopened.Loaded.Notes.Single().Text=="TRUE","Mapped scalar candidate survives encrypted reopen");await Field<Task>(main,"recentTask");byte[] committed=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));
            Require(!await Import(c=>{using var zip=ZipFile.Open(input,ZipArchiveMode.Update);using(var output=zip.CreateEntry("changed.xml").Open())new XDocument(new XElement("changed")).Save(output);return Select(c);},_=>throw new Exception("Changed source must fail before plaintext confirmation"))&&active.Workspace.Notes.Count==1&&File.ReadAllBytes(Path.Combine(root,"vault","current.vault")).SequenceEqual(committed),"Source replacement during mapping refuses whole candidate");File.WriteAllBytes(input,original);
            Require(!await Import(c=>{active.Workspace.Notes[0].Text="newer edit";return Select(c);},_=>throw new Exception("Stale source version must fail before confirmation"))&&active.Workspace.Notes.Count==1,"Mapping-time source edit revokes initial command authority");
            Require(!await Import(c=>{SetField(main,"uiEpoch",Field<long>(main,"uiEpoch")+1);return Select(c);},_=>throw new Exception("Stale UI must fail before confirmation"))&&active.Workspace.Notes.Count==1,"Mapping-time UI epoch revokes initial command authority");
            var workers=typeof(MainWindow).GetNestedTypes(BindingFlags.NonPublic).Where(t=>t.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(m=>m.Name.StartsWith("<ReadExcelCatalogAsync>",StringComparison.Ordinal)||m.Name.StartsWith("<ReadSelectedExcelAsync>",StringComparison.Ordinal))).ToArray();Require(workers.Length==2&&workers.All(t=>t.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).All(f=>f.FieldType==typeof(string)||f.FieldType==typeof(CancellationToken)||f.FieldType==typeof(WorkbookImportSelection))),"Compiled catalog/mapped reader workers own only path/selection/token");Require(File.ReadAllBytes(input).SequenceEqual(original),"Actual mapped import retains original source bytes");
            Task locking=Task.CompletedTask;ExcelImportSelectionDialog? modal=null;
            _ = main.Dispatcher.BeginInvoke(new Action(()=>{modal=main.OwnedWindows.OfType<ExcelImportSelectionDialog>().Single();locking=active.LockAsync();}));
            var modalResult=typeof(MainWindow).GetMethod("ShowExcelSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[catalog]);await locking;
            Require(modalResult is null&&modal is not null&&!modal.IsVisible&&modal.Content is null&&modal.Selection is null,"Actual mapping modal is revoked and clears sheet metadata when active session conceals");
        }
        finally{fixture.Clear();if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}

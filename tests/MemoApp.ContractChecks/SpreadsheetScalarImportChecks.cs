using System.IO.Compression;
using System.Xml.Linq;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class SpreadsheetScalarImportChecks
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-xlsx-scalars-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Title="scalar title";note.Text="scalar body";
        try
        {
            string path=Path.Combine(root,"scalar.xlsx");using(var prepared=OfficeTextTransfer.Capture([note],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,path);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))
            {
                var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;XDocument xml;using(var input=entry.Open())xml=XDocument.Load(input);entry.Delete();
                void Cell(string coordinate,string type,string value){var cell=xml.Descendants(ns+"c").Single(c=>(string?)c.Attribute("r")==coordinate);cell.SetAttributeValue("t",type);cell.RemoveNodes();cell.Add(new XElement(ns+"v",value));}
                Cell("A2","n","9007199254740993");Cell("B2","b","1");using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();xml.Save(output);
            }
            byte[] before=File.ReadAllBytes(path);var imported=ExcelTextTransfer.Read(path);VaultChecks.Require(imported.Notes.Single().Title=="9007199254740993"&&imported.Notes.Single().Text=="TRUE"&&File.ReadAllBytes(path).SequenceEqual(before),"Actual bounded workbook reader imports exact long numeric title/boolean body without changing original ZIP");
            workspace.ImportTexts(imported.Notes,null);VaultChecks.Require(workspace.Notes.Count==2&&workspace.Notes[1].Title=="9007199254740993"&&workspace.Notes[1].Text=="TRUE"&&note.Text=="scalar body","Actual scalar import adds whole plain note and preserves original draft");
            Console.WriteLine("PASS: actual XLSX ZIP reader and atomic scalar note import; exact long integer/boolean and original bytes preserved");
        }
        finally{workspace.Clear();Directory.Delete(root,true);}
    }
}

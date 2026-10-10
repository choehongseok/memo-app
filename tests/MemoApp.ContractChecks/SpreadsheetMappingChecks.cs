using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class SpreadsheetMappingChecks
{
    private static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal static void Run()
    {
        var type=typeof(ExcelTextTransfer);var inspect=type.GetMethod("Inspect")??throw new Exception("Workbook sheet catalog API missing");
        var read=type.GetMethod("ReadSelected")??throw new Exception("Explicit worksheet/column mapping API missing");
        var selectionType=type.Assembly.GetType("MemoApp.Core.Transfer.WorkbookImportSelection")??throw new Exception("Workbook selection DTO missing");
        string root=Path.Combine(Path.GetTempPath(),"memo-mapping-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Title="old";note.Text="unchanged";
        try
        {
            string path=Path.Combine(root,"two.xlsx");using(var prepared=OfficeTextTransfer.Capture([note],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,path);
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))
            {
                Change(zip,"xl/workbook.xml",xml=>xml.Root!.Element(Ns+"sheets")!.Add(new XElement(Ns+"sheet",new XAttribute("name","선택 시트"),new XAttribute("sheetId","2"),new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships")+"id","mapping2"))));
                Change(zip,"xl/_rels/workbook.xml.rels",xml=>xml.Root!.Add(new XElement(xml.Root.Name.Namespace+"Relationship",new XAttribute("Id","mapping2"),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),new XAttribute("Target","worksheets/sheet2.xml"))));
                Write(zip,"xl/worksheets/sheet2.xml",new XDocument(new XElement(Ns+"worksheet",new XElement(Ns+"sheetData",Row(1,Cell("C1","n","9007199254740993"),Cell("F1","b","1")),Row(2,Cell("C2","n","+1.20E-03"),Cell("F2","b","0"))))));
            }
            object Catalog()=>inspect.Invoke(null,[path,CancellationToken.None])!;
            string Hash(object catalog)=>(string)catalog.GetType().GetProperty("SourceSha256")!.GetValue(catalog)!;
            object Selection(string hash,int sheet=2,int title=3,int body=6,bool skip=false)=>Activator.CreateInstance(selectionType,[sheet,title,body,skip,hash])!;
            ImportedWorkbookText Read(object selection)=>(ImportedWorkbookText)read.Invoke(null,[path,selection,CancellationToken.None])!;
            void Refused(Action action,string reason){try{action();throw new Exception("Accepted "+reason);}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException or ArgumentException or OperationCanceledException){}}
            var catalog=Catalog();var sheets=(System.Collections.IEnumerable)catalog.GetType().GetProperty("Sheets")!.GetValue(catalog)!;var names=sheets.Cast<object>().Select(s=>(string)s.GetType().GetProperty("Name")!.GetValue(s)!).ToArray();VaultChecks.Require(names.Length==2&&names[1]=="선택 시트","Catalog enumerates actual non-first worksheet");
            byte[] original=File.ReadAllBytes(path);var imported=Read(Selection(Hash(catalog)));VaultChecks.Require(imported.SheetName=="선택 시트"&&imported.Notes.Length==2&&imported.Notes[0].Title=="9007199254740993"&&imported.Notes[0].Text=="TRUE"&&imported.Notes[1].Title=="+1.20E-03"&&imported.Notes[1].Text=="FALSE","Explicit non-first sheet and selected columns preserve no-header numeric lexical/boolean values");
            VaultChecks.Require(Read(Selection(Hash(catalog),skip:true)).Notes.Single().Title=="+1.20E-03","Explicit skip first row drops first data row without header inference");
            Refused(()=>Read(Selection(Hash(catalog),body:3)),"equal columns");Refused(()=>Read(Selection(Hash(catalog),title:16385)),"out-of-range columns");Refused(()=>Read(Selection("")),"missing source hash");Refused(()=>Read(Selection(new string('0',64))),"wrong source hash");
            Refused(()=>inspect.Invoke(null,[path,new CancellationToken(true)]),"cancelled catalog");Refused(()=>read.Invoke(null,[path,Selection(Hash(catalog)),new CancellationToken(true)]),"cancelled mapped read");
            VaultChecks.Require(File.ReadAllBytes(path).SequenceEqual(original),"Catalog and selection reads preserve source bytes");
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Descendants(Ns+"row").First().Add(Cell("B1","n","1")));
            Refused(()=>Read(Selection(Hash(catalog))),"source changed after catalog");catalog=Catalog();
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Descendants(Ns+"row").First().Add(Cell("C1","n","2")));catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"duplicate coordinate");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Descendants(Ns+"row").First().Add(new XElement(Ns+"c",new XAttribute("r","B1"),new XElement(Ns+"f","1+1"),new XElement(Ns+"v","2"))));catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"formula outside selected columns");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Root!.Element(Ns+"sheetData")!.ReplaceNodes(Enumerable.Range(1,101).Select(i=>Row(i,Cell("C"+i,"n",i.ToString()),Cell("F"+i,"b","1")))));catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"101 notes without truncation");VaultChecks.Require(workspace.Notes.Count==1&&note.Text=="unchanged","Refused workbook applies no early candidate rows");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Root!.Element(Ns+"sheetData")!.ReplaceNodes(Row(1,Cell("XFD1","b","1"))));catalog=Catalog();var emptyTitle=Read(Selection(Hash(catalog),title:1,body:16384));VaultChecks.Require(emptyTitle.Notes.Single().Title==""&&emptyTitle.Notes.Single().Text=="TRUE","Maximum legal column and existing empty-title behavior preserved");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Descendants(Ns+"c").First().SetAttributeValue("t","str"));catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"unsupported cached arbitrary cell type");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>xml.Descendants(Ns+"c").First().Element(Ns+"v")!.Value=new string('1',257));catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"mapped title limit");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/worksheets/sheet2.xml",xml=>{var cell=xml.Descendants(Ns+"c").Single(c=>(string?)c.Attribute("r")=="F1");cell.SetAttributeValue("t","n");cell.Element(Ns+"v")!.Value=new string('1',32768);});catalog=Catalog();Refused(()=>Read(Selection(Hash(catalog))),"bounded scalar text");
            File.WriteAllBytes(path,original);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))Change(zip,"xl/_rels/workbook.xml.rels",xml=>xml.Root!.Elements().First().SetAttributeValue("TargetMode","External"));Refused(()=>Catalog(),"external relationship during catalog");
            Console.WriteLine("PASS: explicit spreadsheet sheet/column mapping, exact scalars, source SHA binding and whole-read refusal");
        }
        finally{workspace.Clear();Directory.Delete(root,true);}
    }
    private static XElement Cell(string address,string type,string value)=>new(Ns+"c",new XAttribute("r",address),new XAttribute("t",type),new XElement(Ns+"v",value));
    private static XElement Row(int index,params XElement[] cells)=>new(Ns+"row",new XAttribute("r",index),cells);
    private static void Change(ZipArchive zip,string name,Action<XDocument> change){var entry=zip.GetEntry(name)!;XDocument xml;using(var input=entry.Open())xml=XDocument.Load(input);entry.Delete();change(xml);Write(zip,name,xml);}
    private static void Write(ZipArchive zip,string name,XDocument xml){using var output=zip.CreateEntry(name).Open();xml.Save(output);}
}

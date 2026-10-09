using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Buffers.Binary;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class ExcelImportFailureChecks
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-excel-malformed-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var source=new EditingWorkspace(TimeProvider.System);var note=source.CreateNote();note.Title="합성";note.Text="body";
        try
        {
            string original=Path.Combine(root,"base.xlsx");using(var prepared=OfficeTextTransfer.Capture([note],OfficeTextFormat.Spreadsheet))TextTransfer.WritePrepared(prepared,original);int sequence=0;XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            string Rewrite(string part,Func<string,string> modify){string target=Path.Combine(root,$"variant-{sequence++}.xlsx");File.Copy(original,target);using var zip=ZipFile.Open(target,ZipArchiveMode.Update);var entry=zip.GetEntry(part)!;string xml;using(var reader=new StreamReader(entry.Open(),new UTF8Encoding(false,true)))xml=reader.ReadToEnd();entry.Delete();using(var writer=new StreamWriter(zip.CreateEntry(part).Open(),new UTF8Encoding(false,true)))writer.Write(modify(xml));return target;}
            void Refuse(string path){try{ExcelTextTransfer.Read(path);throw new Exception("Malformed workbook accepted");}catch(Exception e)when(e is InvalidDataException or XmlException or ArgumentException or IOException){}}
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<is>","<f>1+1</f><is>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>{var x=XDocument.Parse(s);var row=x.Descendants(ns+"row").Last();row.Add(new XElement(row.Elements(ns+"c").First()));return x.ToString();}));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>{var x=XDocument.Parse(s);var row=x.Descendants(ns+"row").Last();row.Add(new XElement(ns+"c",new XAttribute("r","F2"),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t","extra"))));return x.ToString();}));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<t xml:space=\"preserve\">body</t>","<t>_xD800_</t>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<t xml:space=\"preserve\">body</t>","<t>_x0001_</t>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<t xml:space=\"preserve\">body</t>","<t>"+new string('x',32768)+"</t>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/_rels/workbook.xml.rels",s=>s.Replace("Target=\"worksheets/sheet1.xml\"","Target=\"https://example.invalid/sheet.xml\" TargetMode=\"External\"",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("?>","?><!DOCTYPE worksheet [<!ENTITY x SYSTEM 'file:///nonexistent'>]>",StringComparison.Ordinal)));
            foreach(var name in new[]{"../outside","XL/WORKBOOK.XML","xl/vbaProject.bin"}){string path=Path.Combine(root,$"variant-{sequence++}.xlsx");File.Copy(original,path);using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)){using var stream=zip.CreateEntry(name).Open();stream.WriteByte(1);}Refuse(path);}
            string shortPart=Path.Combine(root,"declared-short.xlsx");byte[] changed=File.ReadAllBytes(original);int central=changed.AsSpan().IndexOf(new byte[]{0x50,0x4b,0x01,0x02});uint size=BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(central+24));BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(central+24),size-1);File.WriteAllBytes(shortPart,changed);Refuse(shortPart);
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("</worksheet>",string.Concat(Enumerable.Repeat("<ignored/>",100001))+"</worksheet>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/workbook.xml",s=>{var x=XDocument.Parse(s);x.Root!.Add(new XElement(x.Root.Element(ns+"sheets")!));return x.ToString();}));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>{var x=XDocument.Parse(s);x.Root!.Add(new XElement(x.Root.Element(ns+"sheetData")!));return x.ToString();}));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("</worksheet>","<extLst><ext><sheetData/></ext></extLst></worksheet>",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<sheetData>","<sheetData "+string.Join(" ",Enumerable.Range(0,65).Select(i=>$"a{i}=\"x\""))+">",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("<sheetData>","<sheetData a=\""+new string('x',4097)+"\">",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/worksheets/sheet1.xml",s=>s.Replace("</worksheet>",string.Concat(Enumerable.Repeat("<a>",65))+string.Concat(Enumerable.Repeat("</a>",65))+"</worksheet>",StringComparison.Ordinal)));
            Refuse(Rewrite("[Content_Types].xml",s=>s.Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml","application/vnd.ms-office.activeX+xml",StringComparison.Ordinal)));
            Refuse(Rewrite("xl/_rels/workbook.xml.rels",s=>s.Replace("/worksheet\"","/oleObject\"",StringComparison.Ordinal)));
            foreach(string relationshipPart in new[]{"xl/hidden.RELS","xl/hidden.xml"})
            {
                string path=Path.Combine(root,$"variant-{sequence++}.xlsx");File.Copy(original,path);
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))
                {
                    using(var writer=new StreamWriter(zip.CreateEntry(relationshipPart).Open(),new UTF8Encoding(false,true)))writer.Write("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"external\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"https://example.invalid/sheet\" TargetMode=\"External\"/></Relationships>");
                    var types=zip.GetEntry("[Content_Types].xml")!;XDocument doc;using(var stream=types.Open())doc=XDocument.Load(stream);types.Delete();doc.Root!.Add(new XElement(doc.Root.Name.Namespace+"Override",new XAttribute("PartName","/"+relationshipPart),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")));using var output=zip.CreateEntry("[Content_Types].xml").Open();doc.Save(output);
                }
                Refuse(path);
            }
            string sharedPath=Rewrite("xl/worksheets/sheet1.xml",s=>{var x=XDocument.Parse(s);var cell=x.Descendants(ns+"row").Last().Elements(ns+"c").First();cell.SetAttributeValue("t","s");cell.RemoveNodes();cell.Add(new XElement(ns+"v","0"));return x.ToString();});using(var zip=ZipFile.Open(sharedPath,ZipArchiveMode.Update)){using var writer=new StreamWriter(zip.CreateEntry("xl/sharedStrings.xml").Open(),new UTF8Encoding(false,true));writer.Write("<sst xmlns=\""+ns+"\" count=\"99999999\" uniqueCount=\"99999999\"><si><r><rPr><b/></rPr><t>_x005F_x0041_ _xD83D__xDE00_</t></r></si></sst>");}var shared=ExcelTextTransfer.Read(sharedPath);VaultChecks.Require(shared.Notes.Single().Title=="_x0041_ 😀","Declared shared counts ignored; rich text projection and one-pass escape preserve literal and adjacent surrogate");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{ExcelTextTransfer.Read(original,cancelled.Token);throw new Exception("Cancelled import accepted");}catch(OperationCanceledException){}
            Console.WriteLine("PASS: Excel formula/cache, duplicates, extra columns, surrogate/control/cell bounds, external/DTD/traversal/macro/actual-length refusal and shared-string literal decoding");
        }
        finally{source.Clear();Directory.Delete(root,true);}
    }
}

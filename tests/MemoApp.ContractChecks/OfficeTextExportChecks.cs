using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class OfficeTextExportChecks
{
    internal static void Run()
    {
        var type=typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.OfficeTextTransfer")??throw new Exception("Bounded offline Office text export is missing");var capture=type.GetMethod("Capture")!;var formatType=type.Assembly.GetType("MemoApp.Core.Transfer.OfficeTextFormat")!;
        var workspace=new EditingWorkspace(TimeProvider.System);var first=workspace.CreateNote();first.Title="=SUM(1,2) _x0041_ 합성";first.Text=new string('a',32766)+"😀"+new string('b',32768);var second=workspace.CreateNote();second.Title="<합성>";second.Text="한글\t첫 줄\r\n다음 줄";workspace.AcceptPrepared(workspace.Capture());string original=JsonSerializer.Serialize(workspace.Capture());
        string root=Path.Combine(Path.GetTempPath(),"memo-office-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            foreach(string format in new[]{"Spreadsheet","Word"})
            {
                using var prepared=(PreparedTextExport)capture.Invoke(null,[new[]{first,second},Enum.Parse(formatType,format)])!;string path=Path.Combine(root,format=="Spreadsheet"?"notes.xlsx":"notes.docx");TextTransfer.WritePrepared(prepared,path);using var zip=ZipFile.OpenRead(path);VaultChecks.Require(zip.Entries.All(e=>!e.FullName.Contains("vba",StringComparison.OrdinalIgnoreCase))&&zip.Entries.Count is 3 or 5,"Minimal package contains no macro or external executable");
                XDocument Xml(string name){using var stream=zip.GetEntry(name)!.Open();return XDocument.Load(stream);}
                if(format=="Spreadsheet")
                {
                    XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var sheet=Xml("xl/worksheets/sheet1.xml");var rows=sheet.Descendants(ns+"row").ToArray();VaultChecks.Require(rows.Length==3&&sheet.Descendants(ns+"c").All(c=>(string?)c.Attribute("t")=="inlineStr")&&!sheet.Descendants(ns+"f").Any(),"Every formula-looking cell is inert inline string");var cells=rows[1].Elements(ns+"c").Select(c=>c.Descendants(ns+"t").Single().Value).ToArray();VaultChecks.Require(cells[0]=="=SUM(1,2) _x005F_x0041_ 합성"&&cells.Skip(1).Take(3).All(s=>s.Length<=32767)&&string.Concat(cells.Skip(1).Take(3))==first.Text,"Spreadsheet preserves escape-looking text, 65536 body and surrogate pair across bounded cells");
                }
                else
                {
                    XNamespace ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";var document=Xml("word/document.xml");VaultChecks.Require(document.Descendants(ns+"t").Any(t=>t.Value==first.Title)&&document.Descendants(ns+"tab").Any()&&document.Descendants(ns+"br").Any(),"Word package contains exact Unicode title and explicit tab/newline");
                }
            }
            VaultChecks.ExpectFailure(()=>OfficeTextTransfer.Capture(Enumerable.Repeat(first,101),OfficeTextFormat.Spreadsheet),"Bounded selection rejects excess before package");VaultChecks.ExpectFailure(()=>OfficeTextTransfer.Capture([first,first],OfficeTextFormat.Word),"Duplicate selected source refuses");VaultChecks.ExpectFailure(()=>OfficeTextTransfer.Capture([first],(OfficeTextFormat)99),"Unknown Office format refuses");
            using(var prepared=OfficeTextTransfer.Capture([second],OfficeTextFormat.Word)){string collision=Path.Combine(root,"existing.docx");File.WriteAllBytes(collision,[4,5,6]);VaultChecks.ExpectFailure(()=>TextTransfer.WritePrepared(prepared,collision),"Office collision never overwrites");VaultChecks.Require(File.ReadAllBytes(collision).SequenceEqual(new byte[]{4,5,6}),"Existing Office bytes preserved");}
            VaultChecks.Require(JsonSerializer.Serialize(workspace.Capture())==original,"Office exports preserve all source/history fields");
            Console.WriteLine("PASS: bounded offline XLSX/DOCX packages, inert formula text, Unicode/cell splits, fixed relations and original snapshot preservation");
        }
        finally{workspace.Clear();Directory.Delete(root,true);}
    }
}

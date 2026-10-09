using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MemoApp.Core.Editing;
namespace MemoApp.Core.Transfer;
public enum OfficeTextFormat{Spreadsheet,Word}
public static class OfficeTextTransfer
{
    private const string SheetNs="http://schemas.openxmlformats.org/spreadsheetml/2006/main",WordNs="http://schemas.openxmlformats.org/wordprocessingml/2006/main",RelationNs="http://schemas.openxmlformats.org/package/2006/relationships",OfficeRelationNs="http://schemas.openxmlformats.org/officeDocument/2006/relationships",TypesNs="http://schemas.openxmlformats.org/package/2006/content-types";
    public static PreparedTextExport Capture(IEnumerable<NoteDraft> selection,OfficeTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(selection);if(!Enum.IsDefined(format))throw new ArgumentException("Office export format");var notes=selection.Take(101).ToArray();if(notes.Length is <1 or >100||notes.Select(n=>n.Id).Distinct().Count()!=notes.Length)throw new ArgumentException("Office export selection limits");
        int total=0;foreach(var note in notes){using var validated=TextTransfer.Capture(note);total=checked(total+validated.Bytes.Length);if(total>16*1024*1024)throw new InvalidDataException("Office source byte limit");XmlConvert.VerifyXmlChars(note.Title);XmlConvert.VerifyXmlChars(note.Text);}
        using var output=new MemoryStream();
        try
        {
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
            {
                bool sheet=format==OfficeTextFormat.Spreadsheet;
                XmlPart(zip,"[Content_Types].xml",w=>
                {
                    w.WriteStartElement("Types",TypesNs);foreach(var pair in new[]{("rels","application/vnd.openxmlformats-package.relationships+xml"),("xml","application/xml")}){w.WriteStartElement("Default");w.WriteAttributeString("Extension",pair.Item1);w.WriteAttributeString("ContentType",pair.Item2);w.WriteEndElement();}
                    void Override(string name,string type){w.WriteStartElement("Override");w.WriteAttributeString("PartName",name);w.WriteAttributeString("ContentType",type);w.WriteEndElement();}
                    if(sheet){Override("/xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");Override("/xl/worksheets/sheet1.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");}else Override("/word/document.xml","application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");w.WriteEndElement();
                });
                Relationships(zip,"_rels/.rels",OfficeRelationNs+"/officeDocument",sheet?"xl/workbook.xml":"word/document.xml");
                if(sheet)WriteSpreadsheet(zip,notes);else WriteWord(zip,notes);
            }
            if(output.Length>16*1024*1024)throw new InvalidDataException("Office package limit");return new(output.ToArray());
        }
        finally{CryptographicOperations.ZeroMemory(output.GetBuffer());}
    }
    private static void XmlPart(ZipArchive zip,string name,Action<XmlWriter> content)
    {
        using var stream=zip.CreateEntry(name,CompressionLevel.Optimal).Open();using var writer=XmlWriter.Create(stream,new(){Encoding=new UTF8Encoding(false,true),CloseOutput=false,NewLineHandling=NewLineHandling.Entitize});writer.WriteStartDocument();content(writer);writer.WriteEndDocument();
    }
    private static void Relationships(ZipArchive zip,string name,string type,string target)=>XmlPart(zip,name,w=>{w.WriteStartElement("Relationships",RelationNs);w.WriteStartElement("Relationship");w.WriteAttributeString("Id","rId1");w.WriteAttributeString("Type",type);w.WriteAttributeString("Target",target);w.WriteEndElement();w.WriteEndElement();});
    private static string ExcelText(string value)=>Regex.Replace(value,"_x[0-9A-Fa-f]{4}_",m=>"_x005F_"+m.Value[1..],RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    private static string[] BodyCells(string text)
    {
        var parts=new List<string>();int offset=0;while(offset<text.Length){int count=Math.Min(32767,text.Length-offset);if(offset+count<text.Length&&char.IsHighSurrogate(text[offset+count-1]))count--;parts.Add(text.Substring(offset,count));offset+=count;}while(parts.Count<3)parts.Add("");if(parts.Count>3)throw new InvalidDataException("Spreadsheet body cell limit");return parts.ToArray();
    }
    private static void WriteSpreadsheet(ZipArchive zip,NoteDraft[] notes)
    {
        XmlPart(zip,"xl/workbook.xml",w=>{w.WriteStartElement("workbook",SheetNs);w.WriteStartElement("sheets");w.WriteStartElement("sheet");w.WriteAttributeString("name","메모");w.WriteAttributeString("sheetId","1");w.WriteAttributeString("r","id",OfficeRelationNs,"rId1");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();});
        Relationships(zip,"xl/_rels/workbook.xml.rels",OfficeRelationNs+"/worksheet","worksheets/sheet1.xml");
        XmlPart(zip,"xl/worksheets/sheet1.xml",w=>
        {
            w.WriteStartElement("worksheet",SheetNs);w.WriteStartElement("sheetData");int row=0;
            void Row(string[] values)
            {
                w.WriteStartElement("row");w.WriteAttributeString("r",(++row).ToString(System.Globalization.CultureInfo.InvariantCulture));
                for(int column=0;column<values.Length;column++){w.WriteStartElement("c");w.WriteAttributeString("r",((char)('A'+column)).ToString()+row.ToString(System.Globalization.CultureInfo.InvariantCulture));w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is");w.WriteStartElement("t");w.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");w.WriteString(ExcelText(values[column]));w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();
            }
            Row(["제목","본문 1","본문 2","본문 3","원본 모드"]);foreach(var note in notes)Row([note.Title,..BodyCells(note.Text),note.Mode]);w.WriteEndElement();w.WriteEndElement();
        });
    }
    private static void WriteWord(ZipArchive zip,NoteDraft[] notes)=>XmlPart(zip,"word/document.xml",w=>
    {
        w.WriteStartElement("w","document",WordNs);w.WriteStartElement("w","body",WordNs);
        void Paragraph(string text,bool title)
        {
            w.WriteStartElement("w","p",WordNs);w.WriteStartElement("w","r",WordNs);if(title){w.WriteStartElement("w","rPr",WordNs);w.WriteStartElement("w","b",WordNs);w.WriteEndElement();w.WriteEndElement();}
            string normalized=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');int start=0;
            void Text(string value){w.WriteStartElement("w","t",WordNs);w.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");w.WriteString(value);w.WriteEndElement();}
            for(int i=0;i<normalized.Length;i++)if(normalized[i] is '\t' or '\n'){Text(normalized[start..i]);w.WriteStartElement("w",normalized[i]=='\t'?"tab":"br",WordNs);w.WriteEndElement();start=i+1;}Text(normalized[start..]);w.WriteEndElement();w.WriteEndElement();
        }
        foreach(var note in notes){Paragraph(note.Title,true);Paragraph(note.Text,false);Paragraph("",false);}w.WriteEndElement();w.WriteEndElement();
    });
}

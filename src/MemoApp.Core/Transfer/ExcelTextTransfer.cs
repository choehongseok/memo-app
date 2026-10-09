using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace MemoApp.Core.Transfer;
public sealed record ImportedWorkbookText(string SheetName,int SheetCount,ImportedText[] Notes,string SourceSha256);
public static class ExcelTextTransfer
{
    private const int SourceLimit=16*1024*1024,ExpandedLimit=96*1024*1024;
    private static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main",Relations="http://schemas.openxmlformats.org/package/2006/relationships",OfficeRelations="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static ImportedWorkbookText Read(string source,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();source=LocalFilePath.Resolve(source);LocalFilePath.CheckAncestors(source,true);byte[] bytes=[];
        var parts=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        try
        {
            using(var input=LocalRegularFile.Open(source))bytes=BoundedFileReader.Read(input,0,SourceLimit,cancellationToken);
            string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();using var zip=new ZipArchive(new MemoryStream(bytes,false),ZipArchiveMode.Read);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long declared=0;int expanded=0;
            if(zip.Entries.Count>128)throw new InvalidDataException("Workbook part count");
            foreach(var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();string name=entry.FullName;if(!PartName(name)||!names.Add(name)||name.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)||name.Contains("embedding",StringComparison.OrdinalIgnoreCase)||name.Contains("vba",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ambiguous/unsupported workbook part");declared=checked(declared+entry.Length);if(entry.Length<0||declared>ExpandedLimit)throw new InvalidDataException("Workbook expansion limit");
            }
            foreach(var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();int length=checked((int)entry.Length);if(length>ExpandedLimit-expanded)throw new InvalidDataException("Workbook actual expansion limit");byte[] part=new byte[length];bool added=false;
                try{using var stream=entry.Open();ReadExact(stream,part,cancellationToken);if(stream.ReadByte()!=-1)throw new InvalidDataException("Workbook declared part length mismatch");expanded+=length;parts.Add(entry.FullName,part);added=true;}finally{if(!added)CryptographicOperations.ZeroMemory(part);}
            }
            XElement Load(string name)=>Xml(parts.TryGetValue(name,out var data)?data:throw new InvalidDataException("Missing workbook part"),cancellationToken);
            foreach(var part in parts.Where(p=>p.Key.EndsWith(".rels",StringComparison.OrdinalIgnoreCase)))ValidateRelationships(Xml(part.Value,cancellationToken));
            ValidateContentTypes(Load("[Content_Types].xml"),parts,cancellationToken);
            var workbook=Load("xl/workbook.xml");if(workbook.Name!=Main+"workbook"||workbook.Elements(Main+"sheets").Count()!=1||workbook.Elements().Any(e=>e.Name.Namespace!=Main||e.Name.LocalName is not("fileVersion" or "workbookPr" or "workbookProtection" or "bookViews" or "sheets" or "calcPr")))throw new InvalidDataException("Workbook namespace");var sheets=workbook.Element(Main+"sheets")?.Elements(Main+"sheet").ToArray()??[];if(sheets.Length is <1 or >16||workbook.Element(Main+"sheets")!.Elements().Any(e=>e.Name!=Main+"sheet")||sheets.Select(s=>(string?)s.Attribute("name")).Distinct(StringComparer.Ordinal).Count()!=sheets.Length)throw new InvalidDataException("Worksheet count/name ambiguity");
            var first=sheets[0];string sheetName=(string?)first.Attribute("name")??throw new InvalidDataException("Missing sheet name");ValidateText(sheetName,31);string id=(string?)first.Attribute(OfficeRelations+"id")??throw new InvalidDataException("Missing sheet relationship");var rels=Load("xl/_rels/workbook.xml.rels");var rel=rels.Elements(Relations+"Relationship").SingleOrDefault(r=>(string?)r.Attribute("Id")==id)??throw new InvalidDataException("Missing worksheet relationship");if((string?)rel.Attribute("Type")!=OfficeRelations.NamespaceName+"/worksheet")throw new InvalidDataException("First sheet is not text worksheet");string target=(string?)rel.Attribute("Target")??throw new InvalidDataException("Worksheet target");string sheetPath=target.StartsWith('/')?target[1..]:"xl/"+target;if(!PartName(sheetPath))throw new InvalidDataException("Unsafe worksheet target");
            var shared=parts.TryGetValue("xl/sharedStrings.xml",out var sharedBytes)?ReadShared(Xml(sharedBytes,cancellationToken)):[];var sheet=Load(sheetPath);if(sheet.Name!=Main+"worksheet"||sheet.Elements(Main+"sheetData").Count()!=1||sheet.Elements().Any(e=>e.Name.Namespace!=Main||e.Name.LocalName is not("sheetPr" or "dimension" or "sheetViews" or "sheetFormatPr" or "cols" or "sheetData" or "sheetProtection" or "protectedRanges" or "autoFilter" or "mergeCells" or "conditionalFormatting" or "dataValidations" or "printOptions" or "pageMargins" or "pageSetup" or "headerFooter" or "rowBreaks" or "colBreaks" or "ignoredErrors"))||sheet.Descendants().Any(e=>e.Name.LocalName=="f"||e.Name.LocalName.StartsWith("formula",StringComparison.Ordinal)))throw new InvalidDataException("Unsupported worksheet/formula");var rows=sheet.Element(Main+"sheetData")?.Elements().ToArray()??[];if(rows.Length is <1 or >10001||rows.Any(r=>r.Name!=Main+"row"))throw new InvalidDataException("Worksheet rows");
            var result=new List<ImportedText>();string[]? header=null;int previous=0,cells=0,total=0;
            foreach(var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();if(!int.TryParse((string?)row.Attribute("r"),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int rowIndex)||rowIndex<=previous||rowIndex>10001)throw new InvalidDataException("Worksheet row coordinate");previous=rowIndex;var values=new Dictionary<int,string>();
                foreach(var cell in row.Elements())
                {
                    if(cell.Name!=Main+"c"||++cells>10000)throw new InvalidDataException("Worksheet cell limit/markup");var match=Regex.Match((string?)cell.Attribute("r")??"","^([A-Z]{1,3})([1-9][0-9]{0,4})$",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));if(!match.Success||!int.TryParse(match.Groups[2].Value,out int ownRow)||ownRow!=rowIndex)throw new InvalidDataException("Cell coordinate");int column=0;foreach(char ch in match.Groups[1].Value)column=checked(column*26+ch-'A'+1);if(column>16384||values.ContainsKey(column))throw new InvalidDataException("Duplicate cell coordinate");string value=ReadCell(cell,shared);ValidateText(value,32767);values.Add(column,value);
                }
                if(header is null)
                {
                    if(rowIndex!=1)throw new InvalidDataException("Header row must be first");int width=values.Where(p=>p.Value.Length>0).Select(p=>p.Key).DefaultIfEmpty(0).Max();if(width is not(2 or 5))throw new InvalidDataException("Unsupported column headers");header=Enumerable.Range(1,width).Select(i=>values.GetValueOrDefault(i,"")).ToArray();if(!header.SequenceEqual(new[]{"제목","본문"})&&!header.SequenceEqual(new[]{"Title","Body"})&&!header.SequenceEqual(new[]{"제목","본문 1","본문 2","본문 3","원본 모드"}))throw new InvalidDataException("Unsupported text template");continue;
                }
                if(values.Any(v=>v.Key>header.Length&&v.Value.Length>0))throw new InvalidDataException("Unexpected populated columns");if(values.Values.All(v=>v.Length==0))continue;string title=values.GetValueOrDefault(1,"");string body=header.Length==2?values.GetValueOrDefault(2,""):string.Concat(Enumerable.Range(2,3).Select(i=>values.GetValueOrDefault(i,"")));if(header.Length==5&&values.GetValueOrDefault(5,"") is not("plain" or "rich" or "markdown"))throw new InvalidDataException("Unknown source mode");ValidateText(title,256);ValidateText(body,65536);total=checked(total+Encoding.UTF8.GetByteCount(title)+Encoding.UTF8.GetByteCount(body));if(total>SourceLimit||result.Count>=100)throw new InvalidDataException("Imported text aggregate limit");result.Add(new(title,body,hash));
            }
            if(result.Count==0)throw new InvalidDataException("No supported note rows");return new(sheetName,sheets.Length,result.ToArray(),hash);
        }
        finally{foreach(var part in parts.Values)CryptographicOperations.ZeroMemory(part);CryptographicOperations.ZeroMemory(bytes);}
    }
    private static void ReadExact(Stream input,byte[] bytes,CancellationToken token){int offset=0;while(offset<bytes.Length){token.ThrowIfCancellationRequested();int count=input.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));if(count==0)throw new InvalidDataException("Short workbook part");offset+=count;}token.ThrowIfCancellationRequested();}
    private static bool PartName(string name)=>name.Length is >0 and <=256&&!name.StartsWith('/')&&!name.EndsWith('/')&&name.All(c=>char.IsAsciiLetterOrDigit(c)||"_-.[]/".Contains(c))&&name.Split('/').All(p=>p.Length>0&&p is not("." or ".."));
    private static XElement Xml(byte[] bytes,CancellationToken token)
    {
        RawXmlBudget(bytes,token);
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=ExpandedLimit,MaxCharactersFromEntities=0};
        using(var input=new MemoryStream(bytes,false))using(var reader=XmlReader.Create(input,settings)){int nodes=0;while(reader.Read()){token.ThrowIfCancellationRequested();if(++nodes>100000||reader.Depth>64||reader.AttributeCount>64||reader.Value.Length>7*32767)throw new InvalidDataException("Workbook XML node limit");}}
        using var stream=new MemoryStream(bytes,false);using var parsed=XmlReader.Create(stream,settings);return XDocument.Load(parsed).Root??throw new InvalidDataException("Empty workbook XML");
    }
    private static void RawXmlBudget(byte[] bytes,CancellationToken token)
    {
        bool markup=false;byte quote=0;int span=0,attributes=0,text=0,position=0;
        foreach(byte b in bytes)
        {
            if((++position&65535)==0)token.ThrowIfCancellationRequested();
            if(!markup){if(b=='<'){markup=true;span=1;attributes=0;quote=0;text=0;}else if(++text>12*32767)throw new InvalidDataException("Raw XML text budget");continue;}
            if(++span>4096)throw new InvalidDataException("Raw XML markup budget");
            if(quote!=0){if(b==quote)quote=0;}else if(b is (byte)'\'' or (byte)'"')quote=b;else if(b=='='&&++attributes>64)throw new InvalidDataException("Raw XML attribute budget");else if(b=='>'){markup=false;text=0;}
        }
    }
    private static void ValidateContentTypes(XElement root,Dictionary<string,byte[]> parts,CancellationToken token)
    {
        XNamespace ns="http://schemas.openxmlformats.org/package/2006/content-types";if(root.Name!=ns+"Types")throw new InvalidDataException("Package types namespace");
        var allowed=new HashSet<string>(StringComparer.Ordinal){"application/xml","application/vnd.openxmlformats-package.relationships+xml","application/vnd.openxmlformats-package.core-properties+xml","application/vnd.openxmlformats-officedocument.extended-properties+xml","application/vnd.openxmlformats-officedocument.custom-properties+xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml","application/vnd.openxmlformats-officedocument.theme+xml"};
        var defaults=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var overrides=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var item in root.Elements())
        {
            string? type=(string?)item.Attribute("ContentType");if(type is null||!allowed.Contains(type))throw new InvalidDataException("Unsupported active/package content type");
            if(item.Name==ns+"Default"){string? ext=(string?)item.Attribute("Extension");if(string.IsNullOrEmpty(ext)||!ext.All(char.IsAsciiLetterOrDigit)||!defaults.TryAdd(ext,type))throw new InvalidDataException("Duplicate/default content type");}
            else if(item.Name==ns+"Override"){string? name=(string?)item.Attribute("PartName");if(name is null||!name.StartsWith('/')||!PartName(name[1..])||!parts.ContainsKey(name[1..])||!overrides.TryAdd(name[1..],type))throw new InvalidDataException("Ambiguous content type override");}
            else throw new InvalidDataException("Unsupported type declaration");
        }
        foreach(var part in parts)
        {
            if(part.Key=="[Content_Types].xml")continue;string? type=overrides.GetValueOrDefault(part.Key)??defaults.GetValueOrDefault(Path.GetExtension(part.Key).TrimStart('.'));if(type is null)throw new InvalidDataException("Undeclared package part");
            if(part.Key=="xl/workbook.xml"&&type!="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")throw new InvalidDataException("Workbook main content type");
            if(type=="application/vnd.openxmlformats-package.relationships+xml")ValidateRelationships(Xml(part.Value,token));
        }
    }
    private static void ValidateRelationships(XElement root)
    {
        if(root.Name!=Relations+"Relationships")throw new InvalidDataException("Relationships namespace");var ids=new HashSet<string>(StringComparer.Ordinal);int count=0;
        foreach(var rel in root.Elements())
        {
            string? id=(string?)rel.Attribute("Id"),target=(string?)rel.Attribute("Target"),mode=(string?)rel.Attribute("TargetMode");string? type=(string?)rel.Attribute("Type");bool supported=type is not null&&(new[]{"officeDocument","worksheet","sharedStrings","styles","theme","extended-properties","custom-properties"}.Any(t=>type==OfficeRelations.NamespaceName+"/"+t)||type==Relations.NamespaceName+"/metadata/core-properties");if(!supported||++count>1024||rel.Name!=Relations+"Relationship"||string.IsNullOrEmpty(id)||!ids.Add(id)||target is null||mode is not(null or "Internal")||!PartName(target.StartsWith('/')?target[1..]:target))throw new InvalidDataException("External/ambiguous relationship refused");
        }
    }
    private static string[] ReadShared(XElement root)
    {
        if(root.Name!=Main+"sst")throw new InvalidDataException("Shared string namespace");var values=new List<string>();int bytes=0;foreach(var item in root.Elements()){if(item.Name!=Main+"si"||values.Count>=10000)throw new InvalidDataException("Shared string limit/markup");string value=InlineText(item);ValidateText(value,32767);bytes=checked(bytes+Encoding.UTF8.GetByteCount(value));if(bytes>SourceLimit)throw new InvalidDataException("Shared text aggregate limit");values.Add(value);}return values.ToArray();
    }
    private static string ReadCell(XElement cell,string[] shared)
    {
        string? type=(string?)cell.Attribute("t");if(cell.Elements().Any(e=>e.Name!=Main+"v"&&e.Name!=Main+"is"))throw new InvalidDataException("Unsupported cell markup");if(!cell.HasElements)return "";
        if(type=="inlineStr"&&cell.Elements().Count()==1&&cell.Element(Main+"is") is XElement inline)return InlineText(inline);
        if(type=="s"&&cell.Elements().Count()==1&&cell.Element(Main+"v") is XElement value&&!value.HasElements&&int.TryParse(value.Value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int index)&&index>=0&&index<shared.Length)return shared[index];
        return SpreadsheetScalarText.Read(cell);
    }
    private static string InlineText(XElement item)
    {
        var children=item.Elements().ToArray();if(children.Length>0&&!(children.Length==1&&children[0].Name==Main+"t")&&!children.All(c=>c.Name==Main+"r"))throw new InvalidDataException("Ambiguous string containers");
        var output=new StringBuilder();foreach(var child in children)
        {
            if(child.Name==Main+"t"&&!child.HasElements)output.Append(child.Value);
            else if(child.Name==Main+"r"&&child.Elements().All(e=>e.Name==Main+"rPr"||e.Name==Main+"t")&&child.Elements(Main+"t").Count()==1&&child.Elements(Main+"rPr").Count()<=1&&!child.Element(Main+"t")!.HasElements)output.Append(child.Element(Main+"t")!.Value);
            else throw new InvalidDataException("Unsupported string markup");if(output.Length>7*32767)throw new InvalidDataException("Encoded cell limit");
        }
        return Regex.Replace(output.ToString(),"_x([0-9A-Fa-f]{4})_",m=>((char)Convert.ToInt32(m.Groups[1].Value,16)).ToString(),RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    }
    private static void ValidateText(string text,int limit){if(text.Length>limit||text.Any(c=>char.IsControl(c)&&c is not('\t' or '\r' or '\n')))throw new InvalidDataException("Decoded text limit/control");_ = new UTF8Encoding(false,true).GetByteCount(text);}
}

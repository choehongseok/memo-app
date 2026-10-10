namespace MemoApp.Core.Documents;
// Owned immutable source, including whitespace/escapes/order and opaque future fields.
public sealed record StyledDocument(int SchemaVersion,string SourceJson);
public sealed record RichDocumentInfo(bool Supported,string? Text,string Limitation);
public sealed record StoredInlineImage(int BlockIndex,Guid AttachmentId,string Alt);
public static partial class RichDocumentCodec
{
    public const int MaxSourceBytes=1024*1024,MaxText=65536;
    private static readonly System.Text.UTF8Encoding Utf8=new(false,true);
    public static bool IsWellFormedUnicode(string text)
    {
        for(int i=0;i<text.Length;i++)
            if(char.IsHighSurrogate(text[i])){if(i+1>=text.Length||!char.IsLowSurrogate(text[++i]))return false;}
            else if(char.IsLowSurrogate(text[i]))return false;
        return true;
    }
    public static StyledDocument FromPlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);if(text.Length>MaxText||!IsWellFormedUnicode(text))throw new InvalidDataException("Invalid rich source text");
        var normalized=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');if(normalized.Count(c=>c=='\n')>=1024)throw new InvalidDataException("Rich paragraph limit");
        var lines=normalized.Split('\n');
        var nodes=lines.Select(line=>new{type="paragraph",runs=new[]{new{text=line}}});
        var result=new StyledDocument(1,System.Text.Json.JsonSerializer.Serialize(new{nodes}));Inspect(result);return result;
    }
    public static RichDocumentInfo Inspect(StyledDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if(document.SchemaVersion is not(1 or 2)||document.SourceJson is null||!IsWellFormedUnicode(document.SourceJson)||Utf8.GetByteCount(document.SourceJson)>MaxSourceBytes)throw new InvalidDataException("Unsupported/bounded rich document");
        using var parsed=System.Text.Json.JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});
        ValidateJson(parsed.RootElement);
        var root=parsed.RootElement;if(root.ValueKind!=System.Text.Json.JsonValueKind.Object||!root.TryGetProperty("nodes",out var blocks)||blocks.ValueKind!=System.Text.Json.JsonValueKind.Array||blocks.GetArrayLength()>1024)throw new InvalidDataException("Rich nodes required/bounded");
        bool supported=true;int nodes=0,runs=0;var text=new System.Text.StringBuilder();
        void Fields(System.Text.Json.JsonElement value,string[] known)
        {
            if(value.ValueKind!=System.Text.Json.JsonValueKind.Object)throw new InvalidDataException("Rich object required");
            if(value.EnumerateObject().Any(p=>!known.Contains(p.Name,StringComparer.Ordinal))){if(document.SchemaVersion==2)throw new InvalidDataException("Unknown v2 document fields");supported=false;}
        }
        string String(System.Text.Json.JsonElement value,string name,int bound)
        {
            if(!value.TryGetProperty(name,out var field)||field.ValueKind!=System.Text.Json.JsonValueKind.String)throw new InvalidDataException("Rich string required");
            string result=field.GetString()!;if(result.Length>bound||!IsWellFormedUnicode(result))throw new InvalidDataException("Rich string outside bounds");return result;
        }
        bool Flag(System.Text.Json.JsonElement value,string name)
        {
            if(!value.TryGetProperty(name,out var field)||field.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))throw new InvalidDataException("Rich boolean required");return field.GetBoolean();
        }
        void Append(string value)
        {
            if(value.Length>MaxText-text.Length)throw new InvalidDataException("Rich text outside bounds");text.Append(value);
        }
        void CountNode(){if(++nodes>1024)throw new InvalidDataException("Rich node limit");}
        void ReadRuns(System.Text.Json.JsonElement value)
        {
            if(!value.TryGetProperty("runs",out var array)||array.ValueKind!=System.Text.Json.JsonValueKind.Array)throw new InvalidDataException("Rich runs required");
            foreach(var run in array.EnumerateArray())
            {
                if(++runs>4096)throw new InvalidDataException("Rich run limit");
                Fields(run,["text","bold","underline","strike","fontFamily","fontSize","foreground","background","link"]);Append(String(run,"text",MaxText));
                foreach(var flag in new[]{"bold","underline","strike"})if(run.TryGetProperty(flag,out _))Flag(run,flag);
                if(run.TryGetProperty("fontSize",out var size)&&(!size.TryGetDouble(out double fontSize)||!double.IsFinite(fontSize)||fontSize is <8 or >96))throw new InvalidDataException("Invalid rich font size");
                foreach(var color in new[]{"foreground","background"})if(run.TryGetProperty(color,out _))
                {
                    string hex=String(run,color,9);if(hex.Length is not (7 or 9)||hex[0]!='#'||hex.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))throw new InvalidDataException("Invalid rich color");
                }
                if(run.TryGetProperty("fontFamily",out _))
                {
                    string font=String(run,"fontFamily",128);if(font.Length==0)throw new InvalidDataException("Empty rich font");
                    if(font.Any(c=>!char.IsLetterOrDigit(c)&&c is not (' ' or '-' or '_' or '.')))supported=false;
                }
                if(run.TryGetProperty("link",out _))
                {
                    string link=String(run,"link",2048);if(!Uri.TryCreate(link,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length!=0||link.Any(char.IsControl))supported=false;
                }
            }
        }
        Fields(root,["nodes"]);bool firstBlock=true;
        foreach(var block in blocks.EnumerateArray())
        {
            CountNode();if(!firstBlock)Append("\n");firstBlock=false;
            string type=String(block,"type",128);
            switch(type)
            {
                case "image" when document.SchemaVersion==2:
                    Fields(block,["type","attachmentId","alt"]);string encoded=String(block,"attachmentId",36),alt=String(block,"alt",256);
                    if(encoded.Length!=36||!Guid.TryParseExact(encoded,"D",out var attachmentId)||attachmentId==Guid.Empty||encoded!=attachmentId.ToString("D")||alt.Length==0||alt.Trim()!=alt||alt.Any(char.IsControl))throw new InvalidDataException("Invalid canonical image identity/alt");
                    Append("[이미지: "+alt+"]");break;
                case "paragraph":Fields(block,["type","runs"]);ReadRuns(block);break;
                case "list":case "checklist":
                    Fields(block,type=="list"?["type","ordered","items"]:["type","items"]);if(type=="list")Flag(block,"ordered");
                    if(!block.TryGetProperty("items",out var items)||items.ValueKind!=System.Text.Json.JsonValueKind.Array||items.GetArrayLength() is <1 or >1024)throw new InvalidDataException("Rich list items required/bounded");
                    bool firstItem=true;foreach(var item in items.EnumerateArray())
                    {
                        CountNode();if(!firstItem)Append("\n");firstItem=false;Fields(item,type=="list"?["runs"]:["runs","checked"]);
                        if(type=="checklist")Append(Flag(item,"checked")?"[x] ":"[ ] ");ReadRuns(item);
                    }
                    break;
                case "table":
                    Fields(block,["type","rows"]);if(!block.TryGetProperty("rows",out var rows)||rows.ValueKind!=System.Text.Json.JsonValueKind.Array||rows.GetArrayLength() is <1 or >32)throw new InvalidDataException("Rich table rows required/bounded");
                    int columns=-1;bool firstRow=true;foreach(var row in rows.EnumerateArray())
                    {
                        CountNode();if(row.ValueKind!=System.Text.Json.JsonValueKind.Array||row.GetArrayLength() is <1 or >16||columns!=-1&&row.GetArrayLength()!=columns)throw new InvalidDataException("Rich table columns invalid");columns=row.GetArrayLength();
                        if(!firstRow)Append("\n");firstRow=false;bool firstCell=true;foreach(var cell in row.EnumerateArray()){CountNode();if(!firstCell)Append("\t");firstCell=false;Fields(cell,["runs"]);ReadRuns(cell);}
                    }
                    break;
                default:if(document.SchemaVersion==2)throw new InvalidDataException("Unknown v2 document node");supported=false;break;
            }
        }
        if(document.SchemaVersion==2&&!supported)throw new InvalidDataException("Unsupported v2 document value");
        return new(supported,supported?text.ToString():null,supported?"":"미지원 문서/서식 — 원문과 인증된 본문을 보존하며 읽기 전용입니다.");
    }
    private static void ValidateJson(System.Text.Json.JsonElement root)
    {
        var stack=new Stack<System.Text.Json.JsonElement>();int scheduled=0;
        void Push(System.Text.Json.JsonElement item){if(++scheduled>16384)throw new InvalidDataException("Rich JSON token budget");stack.Push(item);}
        Push(root);
        while(stack.TryPop(out var value))
        {
            if(value.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in value.EnumerateObject())
                {
                    if(!names.Add(property.Name)||!IsWellFormedUnicode(property.Name))throw new InvalidDataException("Duplicate/invalid rich field");Push(property.Value);
                }
            }
            else if(value.ValueKind==System.Text.Json.JsonValueKind.Array)foreach(var item in value.EnumerateArray())Push(item);
            else if(value.ValueKind==System.Text.Json.JsonValueKind.String)
            {
                string text;try{text=value.GetString()!;}catch(InvalidOperationException){throw new InvalidDataException("Invalid rich Unicode escape");}
                if(!IsWellFormedUnicode(text))throw new InvalidDataException("Invalid rich Unicode");
            }
        }
    }
}

using System.Globalization;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;

namespace MemoApp.Core.Transfer;

// N04 known canonical rich source only. URLs are visible literals, never external relations.
// Capture finishes on the source owner; existing workers receive only PreparedTextExport.
public static class StructuredWordExport
{
    private const int Limit = 16 * 1024 * 1024;
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static PreparedTextExport Capture(IEnumerable<NoteDraft> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var notes = selection.Take(101).ToArray();
        if (notes.Length is < 1 or > 100 || notes.Any(n => n is null) || notes.Select(n => n.Id).Distinct().Count() != notes.Length)
            throw new ArgumentException("Word export selection limits");
        int sourceBytes = 0, canonicalBytes = 0;
        foreach (var note in notes)
        {
            if(note.Mode == "rich" && note.Document?.SchemaVersion != 1)
                throw new InvalidOperationException("Word export does not support version2 image documents. Original document and attachments are preserved.");
            using var validated = TextTransfer.Capture(note);
            sourceBytes = checked(sourceBytes + validated.Bytes.Length);
            if (sourceBytes > Limit) throw new InvalidDataException("Word source byte limit");
            XmlConvert.VerifyXmlChars(note.Title); XmlConvert.VerifyXmlChars(note.Text);
            if (note.Mode == "rich")
            {
                // TextTransfer already rejects unknown canonical source; retain that boundary.
                canonicalBytes = checked(canonicalBytes + new UTF8Encoding(false, true).GetByteCount(note.Document!.SourceJson));
                if (canonicalBytes > Limit) throw new InvalidDataException("Word canonical source byte limit");
            }
        }
        return BuildPackage(notes.Select(n=>new WordNoteSource(n.Title,n.Text,n.Mode,n.Document)).ToImmutableArray(),null,default,null);
    }

    internal static WordImageBuildOperation StartOwned(OwnedWordImageContext context,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(context);var state=new WordBuildState(context,token);
        try{context.BeginBuild(state.Operation.Completion,state.Operation.Settled);}
        catch{state.ReleaseBeforeLaunch();throw;}
        try
        {
            if(!ThreadPool.UnsafeQueueUserWorkItem(static (WordBuildState work)=>work.Run(),state,false))throw new IOException("Word worker queue refused");
        }
        catch(Exception error){state.QueueFailed(error);}
        return state.Operation;
    }
    internal static PreparedTextExport BuildOwned(OwnedWordImageContext context,CancellationToken token=default,Action<byte[]>? allocations=null)
    {
        ArgumentNullException.ThrowIfNull(context);Check(context,token);ValidateOwned(context,token);
        // Decode every unique source sequentially before any ZIP/media construction. Pixels are never exported.
        foreach(var image in context.Images)
        {
            Check(context,token);context.ReadImage(image.ObjectId,bytes=>{using var raster=PngPixelDecoder.Decode(bytes,token,allocations);Check(context,token);});Check(context,token);
        }
        return BuildPackage(context.Notes,context,token,allocations);
    }
    private static string Normalize(string text)=>text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
    private static void Check(OwnedWordImageContext? context,CancellationToken token)
    {token.ThrowIfCancellationRequested();if(context?.IsRevoked==true)throw new OperationCanceledException("Word source context revoked");}
    private static void ValidateOwned(OwnedWordImageContext context,CancellationToken token)
    {
        if(context.Notes.Length is <1 or >100||context.Images.Length>128||context.Placements.Length>102400)throw new InvalidDataException("Word detached source bounds");
        long media=0,pixels=0,source=0,canonical=0;var ids=new HashSet<Guid>();
        foreach(var image in context.Images)
        {
            Check(context,token);if(image.ObjectId==Guid.Empty||image.RootId==Guid.Empty||!ids.Add(image.ObjectId))throw new InvalidDataException("Word media identity");
            context.ReadImage(image.ObjectId,bytes=>
            {
                if(bytes.Length!=image.Length||bytes.Length>4194304||Convert.ToHexStringLower(SHA256.HashData(bytes))!=image.Sha256)throw new InvalidDataException("Word exact media descriptor");
                var header=PngPreviewProfile.Inspect(bytes);
                if(header.Width!=image.Width||header.Height!=image.Height||header.SourcePixels!=image.SourcePixels)throw new InvalidDataException("Word source geometry changed");
                media=checked(media+bytes.Length);pixels=checked(pixels+header.SourcePixels);
                if(media>8388608||pixels>64000000)throw new InvalidDataException("Word unique media or source pixel budget");
            });
        }
        var placements=context.Placements.ToDictionary(p=>(p.NoteIndex,p.BlockIndex));int seen=0;
        for(int index=0;index<context.Notes.Length;index++)
        {
            Check(context,token);var note=context.Notes[index];
            if(note.Title.Length>256||note.Text.Length>65536||note.Title.Contains('\0')||note.Text.Contains('\0')||!RichDocumentCodec.IsWellFormedUnicode(note.Title)||!RichDocumentCodec.IsWellFormedUnicode(note.Text)||note.Mode is not("plain" or "markdown" or "rich"))throw new InvalidDataException("Word scalar source");
            XmlConvert.VerifyXmlChars(note.Title);XmlConvert.VerifyXmlChars(note.Text);
            string payload=(note.Title.Length==0?"":Normalize(note.Title)+"\n\n")+Normalize(note.Text);
            source=checked(source+new UTF8Encoding(false,true).GetByteCount(payload.Replace("\n","\r\n",StringComparison.Ordinal)));if(source>Limit)throw new InvalidDataException("Word projected source budget");
            if(note.Mode!="rich")continue;
            if(note.Document is null||note.Document.SchemaVersion is not(1 or 2))throw new InvalidDataException("Word rich source version");
            var inspected=RichDocumentCodec.Inspect(note.Document);if(!inspected.Supported||inspected.Text!=note.Text)throw new InvalidDataException("Word rich projection authority");
            canonical=checked(canonical+new UTF8Encoding(false,true).GetByteCount(note.Document.SourceJson));if(canonical>Limit)throw new InvalidDataException("Word canonical source budget");
            foreach(var block in RichDocumentCodec.Images(note.Document))
            {
                if(!placements.TryGetValue((index,block.BlockIndex),out var placement)||placement.ObjectId!=block.AttachmentId||placement.Alt!=block.Alt||!ids.Contains(placement.ObjectId))throw new InvalidDataException("Word complete image placement authority");
                XmlConvert.VerifyXmlChars(placement.Alt);seen++;
            }
        }
        if(seen!=placements.Count)throw new InvalidDataException("Word extra detached placement");
        if(!ids.SetEquals(context.Placements.Select(p=>p.ObjectId)))throw new InvalidDataException("Word unreferenced detached media");
    }
    private static PreparedTextExport BuildPackage(ImmutableArray<WordNoteSource> notes,OwnedWordImageContext? images,CancellationToken token,Action<byte[]>? allocations)
    {
        Check(images,token);using var output = new BoundedWordBuffer(allocations);
        byte[]? final=null;
        try
        {
        long xmlBytes = 0;
        var mediaIds=images?.Images.Select((image,index)=>(image.ObjectId,Id:index+1)).ToDictionary(i=>i.ObjectId,i=>i.Id)??new Dictionary<Guid,int>();
        var placements=images?.Placements.ToDictionary(p=>(p.NoteIndex,p.BlockIndex))??new Dictionary<(int,int),WordImagePlacement>();
        int drawingId=0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Part(string name, Action<XmlWriter> content)
            {
                Check(images,token);using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                using var bounded = new XmlBudgetStream(entry, count =>
                {
                    Check(images,token);if (count > Limit - xmlBytes) throw new IOException("Word XML construction limit");
                    xmlBytes += count;
                });
                using var writer = XmlWriter.Create(bounded, new() { Encoding = new UTF8Encoding(false, true), CloseOutput = false, NewLineHandling = NewLineHandling.Entitize });
                writer.WriteStartDocument(); content(writer); writer.WriteEndDocument();
            }
            Part("[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", Types);
                foreach (var pair in new[] { ("rels", "application/vnd.openxmlformats-package.relationships+xml"), ("xml", "application/xml") })
                { writer.WriteStartElement("Default", Types); writer.WriteAttributeString("Extension", pair.Item1); writer.WriteAttributeString("ContentType", pair.Item2); writer.WriteEndElement(); }
                if(images is not null&&images.Images.Length>0){writer.WriteStartElement("Default",Types);writer.WriteAttributeString("Extension","png");writer.WriteAttributeString("ContentType","image/png");writer.WriteEndElement();}
                foreach (var pair in new[] { ("document", "document.main"), ("numbering", "numbering") })
                { writer.WriteStartElement("Override", Types); writer.WriteAttributeString("PartName", "/word/" + pair.Item1 + ".xml"); writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml." + pair.Item2 + "+xml"); writer.WriteEndElement(); }
                writer.WriteEndElement();
            });
            void Relationship(XmlWriter writer, string type, string target)
            {
                writer.WriteStartElement("Relationships", Relations); writer.WriteStartElement("Relationship", Relations);
                writer.WriteAttributeString("Id", "rId1"); writer.WriteAttributeString("Type", OfficeRelations + "/" + type); writer.WriteAttributeString("Target", target);
                writer.WriteEndElement(); writer.WriteEndElement();
            }
            Part("_rels/.rels", writer => Relationship(writer, "officeDocument", "word/document.xml"));
            Part("word/_rels/document.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships",Relations);
                void Rel(string id,string type,string target){writer.WriteStartElement("Relationship",Relations);writer.WriteAttributeString("Id",id);writer.WriteAttributeString("Type",OfficeRelations+"/"+type);writer.WriteAttributeString("Target",target);writer.WriteEndElement();}
                Rel("rId1","numbering","numbering.xml");
                if(images is not null)foreach(var image in images.Images){int id=mediaIds[image.ObjectId];Rel("rIdImage"+id.ToString(CultureInfo.InvariantCulture),"image","media/image"+id.ToString("D4",CultureInfo.InvariantCulture)+".png");}
                writer.WriteEndElement();
            });
            // Each list has a separate num instance so ordered lists restart at one.
            var lists = new List<(int Id, bool Ordered)>();
            Part("word/document.xml", writer =>
            {
                Start(writer, "document"); Start(writer, "body");
                for(int noteIndex=0;noteIndex<notes.Length;noteIndex++)
                {
                    Check(images,token);var note=notes[noteIndex];Start(writer, "p"); WriteRun(writer, note.Title, title: true); writer.WriteEndElement();
                    if (note.Mode != "rich") { Start(writer, "p"); WriteRun(writer, note.Text); writer.WriteEndElement(); }
                    else
                    {
                        using var json = JsonDocument.Parse(note.Document!.SourceJson, new() { MaxDepth = 16 });
                        int blockIndex=0;foreach (var block in json.RootElement.GetProperty("nodes").EnumerateArray())
                        {
                            Check(images,token);int currentBlock=blockIndex++;string type = block.GetProperty("type").GetString()!;
                            switch (type)
                            {
                                case "image":
                                    if(images is null||!placements.TryGetValue((noteIndex,currentBlock),out var placement))throw new InvalidDataException("Word image source authority missing");
                                    var descriptor=images.Images.Single(i=>i.ObjectId==placement.ObjectId);int imageId=mediaIds[placement.ObjectId];
                                    WordImageDrawing.Write(writer,checked(++drawingId),"rIdImage"+imageId.ToString(CultureInfo.InvariantCulture),descriptor.Width,descriptor.Height,placement.Alt);break;
                                case "paragraph": Paragraph(writer, block); break;
                                case "list":
                                    int id = lists.Count + 1; lists.Add((id, block.GetProperty("ordered").GetBoolean()));
                                    foreach (var item in block.GetProperty("items").EnumerateArray()) Paragraph(writer, item, id);
                                    break;
                                case "checklist":
                                    foreach (var item in block.GetProperty("items").EnumerateArray()) Paragraph(writer, item, prefix: item.GetProperty("checked").GetBoolean() ? "[x] " : "[ ] ");
                                    break;
                                case "table":
                                    Start(writer, "tbl"); Start(writer, "tblPr"); Start(writer, "tblBorders");
                                    foreach (string edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
                                    { Start(writer, edge); Attribute(writer, "val", "single"); Attribute(writer, "sz", "4"); writer.WriteEndElement(); }
                                    writer.WriteEndElement(); writer.WriteEndElement();
                                    int columns = block.GetProperty("rows")[0].GetArrayLength();
                                    Start(writer, "tblGrid"); for (int i = 0; i < columns; i++) { Start(writer, "gridCol"); writer.WriteEndElement(); } writer.WriteEndElement();
                                    foreach (var row in block.GetProperty("rows").EnumerateArray())
                                    {
                                        Start(writer, "tr"); foreach (var cell in row.EnumerateArray()) { Start(writer, "tc"); Paragraph(writer, cell); writer.WriteEndElement(); } writer.WriteEndElement();
                                    }
                                    writer.WriteEndElement(); break;
                                default: throw new InvalidDataException("Unknown Word source block");
                            }
                        }
                    }
                    Start(writer, "p"); writer.WriteEndElement();
                }
                if(images is not null)WordImageDrawing.Section(writer);
                writer.WriteEndElement(); writer.WriteEndElement();
            });
            Part("word/numbering.xml", writer =>
            {
                Start(writer, "numbering");
                for (int abstractId = 0; abstractId < 2; abstractId++)
                {
                    Start(writer, "abstractNum"); Attribute(writer, "abstractNumId", abstractId.ToString(CultureInfo.InvariantCulture));
                    Property(writer, "multiLevelType", "singleLevel"); Start(writer, "lvl"); Attribute(writer, "ilvl", "0");
                    Property(writer, "start", "1"); Property(writer, "numFmt", abstractId == 0 ? "decimal" : "bullet"); Property(writer, "lvlText", abstractId == 0 ? "%1." : "•"); Property(writer, "lvlJc", "left");
                    Start(writer, "pPr"); Start(writer, "ind"); Attribute(writer, "left", "720"); Attribute(writer, "hanging", "360"); writer.WriteEndElement(); writer.WriteEndElement();
                    writer.WriteEndElement(); writer.WriteEndElement();
                }
                foreach (var list in lists)
                { Start(writer, "num"); Attribute(writer, "numId", list.Id.ToString(CultureInfo.InvariantCulture)); Property(writer, "abstractNumId", list.Ordered ? "0" : "1"); writer.WriteEndElement(); }
                writer.WriteEndElement();
            });
            if(images is not null)foreach(var image in images.Images)
            {
                Check(images,token);int id=mediaIds[image.ObjectId];using var entry=zip.CreateEntry("word/media/image"+id.ToString("D4",CultureInfo.InvariantCulture)+".png",CompressionLevel.NoCompression).Open();
                images.ReadImage(image.ObjectId,bytes=>{for(int offset=0;offset<bytes.Length;offset+=65536){Check(images,token);entry.Write(bytes.Slice(offset,Math.Min(65536,bytes.Length-offset)));}});
            }
            Check(images,token);
        } // ZIP central directory is finalized while the same hard-bounded zeroing buffer is live.
        Check(images,token);final=output.ToArray();allocations?.Invoke(final);Check(images,token);
        var prepared=new PreparedTextExport(final);final=null;return prepared;
        }
        finally{if(final is not null)CryptographicOperations.ZeroMemory(final);}
    }

    private static void Start(XmlWriter writer, string name) => writer.WriteStartElement("w", name, W);
    private static void Attribute(XmlWriter writer, string name, string value) => writer.WriteAttributeString("w", name, W, value);
    private static void Property(XmlWriter writer, string name, string value) { Start(writer, name); Attribute(writer, "val", value); writer.WriteEndElement(); }
    private static void Paragraph(XmlWriter writer, JsonElement item, int? list = null, string? prefix = null)
    {
        Start(writer, "p");
        if (list is int id)
        { Start(writer, "pPr"); Start(writer, "numPr"); Property(writer, "ilvl", "0"); Property(writer, "numId", id.ToString(CultureInfo.InvariantCulture)); writer.WriteEndElement(); writer.WriteEndElement(); }
        if (prefix is not null) WriteRun(writer, prefix);
        foreach (var run in item.GetProperty("runs").EnumerateArray())
        {
            WriteRun(writer, run.GetProperty("text").GetString()!, run);
            if (run.TryGetProperty("link", out var link)) WriteRun(writer, " <" + link.GetString()! + ">");
        }
        writer.WriteEndElement();
    }
    private static void WriteRun(XmlWriter writer, string text, JsonElement? style = null, bool title = false)
    {
        Start(writer, "r");
        if (title || style is not null)
        {
            Start(writer, "rPr");
            if (style is JsonElement run)
            {
                // CT_RPr schema order: fonts, bold, strike, color, size, underline, shading.
                if (run.TryGetProperty("fontFamily", out var font))
                { Start(writer, "rFonts"); foreach (string script in new[] { "ascii", "hAnsi", "eastAsia", "cs" }) Attribute(writer, script, font.GetString()!); writer.WriteEndElement(); }
                if (run.TryGetProperty("bold", out var bold) && bold.GetBoolean()) Property(writer, "b", "1");
                if (run.TryGetProperty("strike", out var strike) && strike.GetBoolean()) Property(writer, "strike", "1");
                if (run.TryGetProperty("foreground", out var color)) Property(writer, "color", WordColor(color.GetString()!));
                if (run.TryGetProperty("fontSize", out var size))
                { string halfPoints = Math.Round(size.GetDouble() * 1.5, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture); Property(writer, "sz", halfPoints); Property(writer, "szCs", halfPoints); }
                if (run.TryGetProperty("underline", out var underline) && underline.GetBoolean()) Property(writer, "u", "single");
                if (run.TryGetProperty("background", out var background))
                { Start(writer, "shd"); Attribute(writer, "val", "clear"); Attribute(writer, "color", "auto"); Attribute(writer, "fill", WordColor(background.GetString()!)); writer.WriteEndElement(); }
            }
            else Property(writer, "b", "1");
            writer.WriteEndElement();
        }
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'); int start = 0;
        void Text(string value) { Start(writer, "t"); writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve"); writer.WriteString(value); writer.WriteEndElement(); }
        for (int i = 0; i < normalized.Length; i++) if (normalized[i] is '\t' or '\n')
        { Text(normalized[start..i]); Start(writer, normalized[i] == '\t' ? "tab" : "br"); writer.WriteEndElement(); start = i + 1; }
        Text(normalized[start..]); writer.WriteEndElement();
    }
    // Word text/shading colors have no alpha. Composite ARGB over the export's white page.
    private static string WordColor(string value)
    {
        if (value.Length == 7) return value[1..].ToUpperInvariant();
        int alpha = int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var result = new StringBuilder(6);
        for (int offset = 3; offset < 9; offset += 2)
        { int component = int.Parse(value.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); result.Append(((component * alpha + 255 * (255 - alpha) + 127) / 255).ToString("X2", CultureInfo.InvariantCulture)); }
        return result.ToString();
    }
    private sealed class XmlBudgetStream(Stream target, Action<int> account) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => target.Flush();
        public override void Write(byte[] buffer, int offset, int count) { account(count); target.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { account(buffer.Length); target.Write(buffer); }
        public override void WriteByte(byte value) { account(1); target.WriteByte(value); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class BoundedWordBuffer : MemoryStream
    {
        private readonly Action<byte[]>? allocations;
        public BoundedWordBuffer() { }
        internal BoundedWordBuffer(Action<byte[]>? allocations) { this.allocations=allocations; }
        private bool cleared;
        private static void Check(long value) { if (value < 0 || value > Limit) throw new IOException("Word package construction limit"); }
        public override int Capacity
        {
            get => base.Capacity;
            set
            {
                Check(value);
                byte[] previous = GetBuffer();
                base.Capacity = value;
                if (!ReferenceEquals(previous, GetBuffer())){CryptographicOperations.ZeroMemory(previous);allocations?.Invoke(GetBuffer());}
            }
        }
        private void Prepare(long end)
        {
            Check(end);
            if (end > Capacity) Capacity = (int)Math.Min(Limit, Math.Max(end, Math.Max(256L, 2L * Capacity)));
        }
        public override long Position { get => base.Position; set { Check(value); base.Position = value; } }
        public override void SetLength(long value) { Prepare(value); base.SetLength(value); }
        public override long Seek(long offset, SeekOrigin origin)
        { Check(checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => Position, SeekOrigin.End => Length, _ => throw new ArgumentException("Seek origin") }) + offset)); return base.Seek(offset, origin); }
        public override void Write(byte[] buffer, int offset, int count) { ArgumentNullException.ThrowIfNull(buffer); ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfNegative(count); if (offset > buffer.Length - count) throw new ArgumentException("Buffer range"); Prepare(checked(Position + count)); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Prepare(checked(Position + buffer.Length)); base.Write(buffer); }
        public override void WriteByte(byte value) { Prepare(checked(Position + 1)); base.WriteByte(value); }
        protected override void Dispose(bool disposing)
        { if (disposing && !cleared) { cleared = true; CryptographicOperations.ZeroMemory(GetBuffer()); } base.Dispose(disposing); }
    }
}

internal sealed record WordImageBuildOperation(Task<PreparedTextExport> Completion,Task Settled);
// Fixed detached worker state: no issuer, live note, key or native callback is retained.
internal sealed class WordBuildState
{
    private readonly OwnedWordImageContext context;
    private readonly CancellationTokenSource linked;
    private readonly TaskCompletionSource<PreparedTextExport> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource settled=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal WordImageBuildOperation Operation{get;}
    internal WordBuildState(OwnedWordImageContext context,CancellationToken token)
    {this.context=context;Operation=new(completion.Task,settled.Task);linked=CancellationTokenSource.CreateLinkedTokenSource(context.Token,token);}
    internal void ReleaseBeforeLaunch()=>linked.Dispose();
    internal void QueueFailed(Exception error)=>Finish(null,error);
    internal void Run()
    {
        PreparedTextExport? result=null;Exception? failure=null;
        try{result=StructuredWordExport.BuildOwned(context,linked.Token);}catch(Exception error){failure=error;}
        Finish(result,failure);
    }
    private void Finish(PreparedTextExport? result,Exception? failure)
    {
        Exception? cleanup=null;
        try{if(!context.CompleteBuild()){result?.Dispose();result=null;failure??=new OperationCanceledException("Word context revoked");}}catch(Exception error){cleanup=error;}
        try{linked.Dispose();}catch(Exception error){cleanup??=error;}
        if(failure is null&&cleanup is null&&result is not null)completion.TrySetResult(result);
        else
        {
            result?.Dispose();if(failure is OperationCanceledException canceled)completion.TrySetCanceled(canceled.CancellationToken);else completion.TrySetException(failure??cleanup??new InvalidDataException("Word build result missing"));
        }
        if(cleanup is null)settled.TrySetResult();else settled.TrySetException(cleanup);
    }
}

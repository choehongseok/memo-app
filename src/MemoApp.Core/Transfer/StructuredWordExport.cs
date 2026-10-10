using System.Globalization;
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
        using var output = new BoundedWordBuffer();
        long xmlBytes = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Part(string name, Action<XmlWriter> content)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                using var bounded = new XmlBudgetStream(entry, count =>
                {
                    if (count > Limit - xmlBytes) throw new IOException("Word XML construction limit");
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
            Part("word/_rels/document.xml.rels", writer => Relationship(writer, "numbering", "numbering.xml"));
            // Each list has a separate num instance so ordered lists restart at one.
            var lists = new List<(int Id, bool Ordered)>();
            Part("word/document.xml", writer =>
            {
                Start(writer, "document"); Start(writer, "body");
                foreach (var note in notes)
                {
                    Start(writer, "p"); WriteRun(writer, note.Title, title: true); writer.WriteEndElement();
                    if (note.Mode != "rich") { Start(writer, "p"); WriteRun(writer, note.Text); writer.WriteEndElement(); }
                    else
                    {
                        using var json = JsonDocument.Parse(note.Document!.SourceJson, new() { MaxDepth = 16 });
                        foreach (var block in json.RootElement.GetProperty("nodes").EnumerateArray())
                        {
                            string type = block.GetProperty("type").GetString()!;
                            switch (type)
                            {
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
        }
        return new(output.ToArray());
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
                if (!ReferenceEquals(previous, GetBuffer())) CryptographicOperations.ZeroMemory(previous);
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

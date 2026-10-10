using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;

// N04: canonical known rich formatting; Word desktop acceptance is a separate check.
internal static class StructuredWordChecks
{
    internal static void Run()
    {
        VersionedImageCapabilityRefusal();
        var exporter = typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.StructuredWordExport");
        VaultChecks.Require(exporter is not null, "N04 canonical rich Word exporter is missing");
        var capture = exporter!.GetMethod("Capture", [typeof(IEnumerable<NoteDraft>)])!;
        VaultChecks.Require(capture is not null, "Canonical Word Capture selection API is missing");
        PreparedTextExport Capture(IEnumerable<NoteDraft> notes)
        {
            try { return (PreparedTextExport)capture!.Invoke(null, [notes])!; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        var workspace = new EditingWorkspace(TimeProvider.System);
        var note = workspace.CreateNote(); note.Title = "합성 <Word> 😀";
        workspace.ConvertMode(note, "rich", true);
        workspace.SetRichDocument(note, new(1, """
        {"nodes":[
          {"type":"paragraph","runs":[{"text":"서식\t첫 줄\n둘째 😀","bold":true,"underline":true,"strike":true,"fontFamily":"D2Coding","fontSize":16,"foreground":"#123456","background":"#FFABCDEF","link":"https://example.invalid/synthetic?a=1&b=2"},{"text":" 해제","bold":false}]},
          {"type":"list","ordered":true,"items":[{"runs":[{"text":"순서 1"}]},{"runs":[{"text":"순서 2","bold":true}]}]},
          {"type":"list","ordered":false,"items":[{"runs":[{"text":"불릿"}]}]},
          {"type":"checklist","items":[{"checked":true,"runs":[{"text":"완료"}]},{"checked":false,"runs":[{"text":"미완료"}]}]},
          {"type":"table","rows":[[{"runs":[{"text":"셀 A","underline":true}]},{"runs":[{"text":"셀 B"}]}],[{"runs":[{"text":"셀 C"}]},{"runs":[{"text":"셀 D"}]}]]}
        ]}
        """));
        workspace.AcceptPrepared(workspace.Capture());
        string before = JsonSerializer.Serialize(workspace.Capture());
        string root = Path.Combine(Path.GetTempPath(), "memo-structured-word-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var prepared = Capture([note]);
            using var routed = OfficeTextTransfer.Capture([note], OfficeTextFormat.Word);
            using(var routedZip=new ZipArchive(new MemoryStream(routed.Bytes,false),ZipArchiveMode.Read))
            using(var routedXml=routedZip.GetEntry("word/document.xml")!.Open())
                VaultChecks.Require(XDocument.Load(routedXml).Descendants(XName.Get("numPr","http://schemas.openxmlformats.org/wordprocessingml/2006/main")).Count()==3,"Production Office Word route uses canonical structured export");
            string path = Path.Combine(root, "rich.docx"); TextTransfer.WritePrepared(prepared, path);
            using var zip = ZipFile.OpenRead(path);
            XDocument Xml(string name) { using var input = zip.GetEntry(name)!.Open(); return XDocument.Load(input); }
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var document = Xml("word/document.xml");
            var run = document.Descendants(w + "r").Single(e => e.Elements(w + "t").Any(t => t.Value == "서식"));
            var properties = run.Element(w + "rPr")!;
            VaultChecks.Require(properties.Element(w + "b") is not null && properties.Element(w + "u")?.Attribute(w + "val")?.Value == "single" && properties.Element(w + "strike") is not null, "Canonical bold underline strike preserved on run");
            VaultChecks.Require(properties.Element(w + "rFonts")?.Attribute(w + "eastAsia")?.Value == "D2Coding" && properties.Element(w + "sz")?.Attribute(w + "val")?.Value == "24", "WPF 16 DIP font becomes 12 point / 24 half-points with Korean font");
            VaultChecks.Require(properties.Element(w + "color")?.Attribute(w + "val")?.Value == "123456" && properties.Element(w + "shd")?.Attribute(w + "fill")?.Value == "ABCDEF", "Canonical foreground and opaque ARGB background preserved");
            var off = document.Descendants(w + "r").Single(e => e.Elements(w + "t").Any(t => t.Value == " 해제"));
            VaultChecks.Require(off.Element(w + "rPr")?.Element(w + "b") is null, "False style does not inherit previous run bold");
            VaultChecks.Require(document.Descendants(w + "tab").Any() && document.Descendants(w + "br").Any() && document.Descendants(w + "t").Any(t => t.Value.Contains("😀")), "Rich Unicode tab and newline survive");
            VaultChecks.Require(document.Descendants(w + "t").Any(t => t.Value.Contains("https://example.invalid/synthetic?a=1&b=2")), "Safe canonical URL preserved visibly as inert literal");
            VaultChecks.Require(document.Descendants(w + "numPr").Count() == 3 && Xml("word/numbering.xml").Descendants(w + "numFmt").Select(e => e.Attribute(w + "val")!.Value).Contains("decimal") && Xml("word/numbering.xml").Descendants(w + "numFmt").Any(e => e.Attribute(w + "val")?.Value == "bullet"), "Ordered and unordered lists have actual Word numbering");
            VaultChecks.Require(document.Descendants(w + "t").Any(t => t.Value == "[x] ") && document.Descendants(w + "t").Any(t => t.Value == "[ ] "), "Checklist states remain inert and visible");
            VaultChecks.Require(document.Descendants(w + "tbl").Count() == 1 && document.Descendants(w + "tr").Count() == 2 && document.Descendants(w + "tc").Count() == 4, "Canonical rectangular table remains actual table");
            VaultChecks.Require(!document.Descendants(w + "fldSimple").Any() && !document.Descendants(w + "instrText").Any() && !document.Descendants().Attributes(r + "id").Any(), "No executable fields or external relationship-backed content");
            VaultChecks.Require(zip.Entries.All(e => e.FullName is "[Content_Types].xml" or "_rels/.rels" or "word/document.xml" or "word/numbering.xml" or "word/_rels/document.xml.rels"), "No arbitrary media, paths, embeddings or active package parts");
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.Ordinal)))
                VaultChecks.Require(Xml(entry.FullName).Root!.Elements().All(e => e.Attribute("TargetMode") is null), "Every package relationship is fixed internal");
            VaultChecks.Require(JsonSerializer.Serialize(workspace.Capture()) == before, "Rich Word export preserves source JSON and full revision history");
            VaultChecks.ExpectFailure(() => Capture([]), "Empty selection refuses");
            VaultChecks.ExpectFailure(() => Capture([note, note]), "Duplicate selection refuses");
            VaultChecks.ExpectFailure(() => Capture(Enumerable.Repeat(note, 101)), "Overlarge selection refuses");
            File.WriteAllBytes(Path.Combine(root, "collision.docx"), [7, 8]);
            VaultChecks.ExpectFailure(() => TextTransfer.WritePrepared(prepared, Path.Combine(root, "collision.docx")), "Structured Word uses CreateNew");
            VaultChecks.Require(File.ReadAllBytes(Path.Combine(root, "collision.docx")).SequenceEqual(new byte[] { 7, 8 }), "Existing destination preserved");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool cancellationRejected = false;
            try { TextTransfer.WritePrepared(prepared, Path.Combine(root, "cancel.docx"), cancelled.Token); }
            catch (OperationCanceledException) { cancellationRejected = true; }
            VaultChecks.Require(cancellationRejected, "Cancelled structured Word refuses before file");
            VaultChecks.Require(!File.Exists(Path.Combine(root, "cancel.docx")), "Cancelled structured Word creates no file");
            var plain = workspace.CreateNote(); plain.Text = "literal =HYPERLINK(\"file:///secret\")\tline\nlast";
            using (var plainExport = Capture([plain])) TextTransfer.WritePrepared(plainExport, Path.Combine(root, "plain.docx"));
            var bufferType = exporter.GetNestedType("BoundedWordBuffer", BindingFlags.NonPublic)!;
            VaultChecks.Require(bufferType is not null, "Structured Word construction has a hard bounded zeroing buffer");
            using (var growth = (MemoryStream)Activator.CreateInstance(bufferType!, true)!)
            {
                growth.WriteByte(91); byte[] previousBacking = growth.GetBuffer();
                growth.Write(new byte[previousBacking.Length + 1]);
                VaultChecks.Require(previousBacking.All(b => b == 0), "Word construction clears retired plaintext backing array during growth");
                VaultChecks.Require(growth.GetBuffer()[0] == 91, "Word growth preserves owned live plaintext bytes");
            }
            var buffer = (MemoryStream)Activator.CreateInstance(bufferType!, true)!;
            buffer.SetLength(16 * 1024 * 1024); buffer.Position = 0; buffer.WriteByte(91); buffer.Position = buffer.Length;
            VaultChecks.ExpectFailure(() => buffer.WriteByte(1), "16MiB package construction rejects before growth");
            byte[] owned = buffer.GetBuffer(); buffer.Dispose(); VaultChecks.Require(owned.All(b => b == 0), "Word construction plaintext zeroed on dispose");
            var now = DateTimeOffset.UtcNow;
            var future = new EditingWorkspace(TimeProvider.System, new MemoApp.Core.Storage.VaultSnapshot(4, Guid.NewGuid(), [new(Guid.NewGuid(), Guid.NewGuid(), [], now, now, "unknown", "authenticated", "rich") { Document = new(1, "{\"nodes\":[{\"type\":\"image\",\"path\":\"file:///secret\"}]}") }]));
            string unknownBefore = JsonSerializer.Serialize(future.Capture());
            VaultChecks.ExpectFailure(() => Capture(future.Notes), "Unknown/image source refuses without invented path placeholder");
            VaultChecks.Require(JsonSerializer.Serialize(future.Capture()) == unknownBefore, "Unknown original preserved on refusal"); future.Clear();
            // Repeated empty rich runs compress well; ZIP size alone cannot bound XML construction.
            string denseRun = "{\"text\":\"\",\"bold\":true,\"underline\":true,\"strike\":true,\"fontFamily\":\"D2Coding\",\"fontSize\":16,\"foreground\":\"#123456\",\"background\":\"#ABCDEF\"}";
            var denseDocument = new StyledDocument(1, "{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[" + string.Join(',', Enumerable.Repeat(denseRun, 768)) + "]}]}");
            VaultChecks.Require(RichDocumentCodec.Inspect(denseDocument).Supported, "Dense XML bound fixture is valid canonical source");
            var denseNotes = Enumerable.Range(0, 100).Select(_ => new MemoApp.Core.Storage.StoredNote(Guid.NewGuid(), Guid.NewGuid(), [], now, now, "bounded", "", "rich") { Document = denseDocument }).ToImmutableArray();
            var dense = new EditingWorkspace(TimeProvider.System, new MemoApp.Core.Storage.VaultSnapshot(4, Guid.NewGuid(), denseNotes.ToArray()));
            VaultChecks.ExpectFailure(() => Capture(dense.Notes), "Compressible rich source exceeds aggregate XML byte budget");
            VaultChecks.Require(dense.Notes.Count == 100 && dense.Notes.All(n => n.Document == denseDocument), "XML construction refusal preserves canonical source"); dense.Clear();
            workspace.Clear(); VaultChecks.ExpectFailure(() => Capture([note]), "Closed rich source refuses");
            Console.WriteLine("PASS: N04 canonical rich DOCX run formatting, inert links, numbered lists/checklists/tables, source fidelity and bounded package; actual Word acceptance unverified");
        }
        finally { workspace.Clear(); Directory.Delete(root, true); }
    }

    private static void VersionedImageCapabilityRefusal()
    {
        // After removing the final image, a document still owns version 2. Word
        // capability must be explicit even when all remaining blocks look like v1.
        Guid id = Guid.NewGuid();
        var documents = new StyledDocument[]
        {
            new(2, "{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"synthetic remaining text\"}]}]}"),
            new(2, "{\"nodes\":[{\"type\":\"image\",\"attachmentId\":\"" + id.ToString("D") + "\",\"alt\":\"synthetic.png\"}]}")
        };
        foreach(var document in documents)
        {
            var info = RichDocumentCodec.Inspect(document);
            VaultChecks.Require(info.Supported, "Word capability fixture is known canonical v2, rather than unknown-source refusal");
            var now = DateTimeOffset.UnixEpoch;
            var stored = new MemoApp.Core.Storage.StoredNote(Guid.NewGuid(), Guid.NewGuid(), [], now, now, "synthetic", info.Text!, "rich")
            { Document = document, AttachmentIds = [id] };
            var note = new NoteDraft(TimeProvider.System, stored);
            string source = note.Document!.SourceJson;
            try
            {
                VaultChecks.ExpectFailure(() => StructuredWordExport.Capture([note]), "Word explicitly refuses version2 before producing a package");
                VaultChecks.ExpectFailure(() => OfficeTextTransfer.Capture([note], OfficeTextFormat.Word), "Production Word route preserves version2 capability refusal");
                VaultChecks.Require(note.Document.SourceJson == source && note.EditVersion == 0 && note.Text == info.Text, "Version2 Word refusal preserves exact source/text/version");
            }
            finally { note.Close(); }
        }
    }
}

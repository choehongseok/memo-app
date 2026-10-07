using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
internal static class TextTransferChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-text-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "합성 제목.txt"); var original = Encoding.UTF8.GetBytes("합성 한글\r\n둘째 줄\r마지막"); File.WriteAllBytes(source, original);
            var imported = TextTransfer.Read(source);
            VaultChecks.Require(imported.Title == "합성 제목" && imported.Text == "합성 한글\n둘째 줄\n마지막" && imported.SourceSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant(), "strict UTF8 import/title/line ending/exact source hash");
            VaultChecks.Require(File.ReadAllBytes(source).SequenceEqual(original), "import must preserve source bytes");
            string utf16 = Path.Combine(root, "utf16.txt"); File.WriteAllText(utf16, "합성 유니코드", Encoding.Unicode);
            VaultChecks.Require(TextTransfer.Read(utf16).Text == "합성 유니코드", "BOM UTF16 import");
            string invalid = Path.Combine(root, "invalid.txt"); File.WriteAllBytes(invalid, [0xFF, 0xFF, 0x80]); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "invalid encoding must reject without guessed ANSI");
            File.WriteAllText(invalid, "a\0b"); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "binary NUL must reject");
            File.WriteAllText(invalid, new string('x', 65537)); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "text length limit must reject");
            File.WriteAllBytes(invalid, new byte[1024 * 1024 + 1]); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "source size limit must reject before unbounded read");
            File.WriteAllText(invalid, new string('x',65536)); VaultChecks.Require(TextTransfer.Read(invalid).Text.Length == 65536, "exact text boundary");
            File.WriteAllText(invalid, new string('x',65535) + "\r\n"); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "pre-normalization text limit must not be bypassed");
            File.WriteAllText(invalid, "합성 BOM", new UTF8Encoding(true,true)); VaultChecks.Require(TextTransfer.Read(invalid).Text == "합성 BOM", "UTF8 BOM strict import");
            File.WriteAllText(invalid, "합성 BE", new UnicodeEncoding(true,true,true)); VaultChecks.Require(TextTransfer.Read(invalid).Text == "합성 BE", "UTF16BE BOM strict import");
            File.WriteAllBytes(invalid, [0xFF,0xFE,0,0,65,0,0,0]); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "UTF32 BOM must not masquerade as UTF16");
            File.WriteAllBytes(invalid, [0xFF,0xFE,0,0xD8]); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "malformed UTF16 surrogate reject");
            File.WriteAllBytes(invalid, [0xFF,0xFE,65]); VaultChecks.ExpectFailure(() => TextTransfer.Read(invalid), "odd UTF16 byte reject");
            foreach (var unsafePath in new[] { "\\\\server\\share\\memo.txt", "file://server/memo.txt", "safe.txt:stream" }) VaultChecks.ExpectFailure(() => TextTransfer.Read(unsafePath), "non-local/device/stream path reject before read");
            if(OperatingSystem.IsWindows())
            {
                foreach(var unsafePath in new[]{@"C:\temp\safe.txt:stream",@"C:\temp\CON.txt",@"C:\temp\COM1.txt",@"\\?\C:\temp\safe.txt",@"\/server/share/memo.txt",@"/\server\share\memo.txt"})
                    VaultChecks.ExpectFailure(()=>MemoApp.Core.Transfer.LocalFilePath.Resolve(unsafePath),"Windows ADS/device/reserved/mixed-UNC must reject without target access");
            }
            var workspace = new EditingWorkspace(TimeProvider.System); var note = workspace.ImportText(imported.Title, imported.Text);
            VaultChecks.Require(workspace.Notes.Single() == note && note.Title == imported.Title && note.Text == imported.Text, "atomic preflight import applies complete draft");
            var captured = workspace.Capture(); workspace.AcceptPrepared(captured); int events = 0; workspace.Changed += () => events++;
            VaultChecks.ExpectFailure(() => workspace.ImportText(new string('x',257), "test"), "import title budget rejects atomically");
            VaultChecks.ExpectFailure(() => workspace.ImportText("test", new string('x',65537)), "import text budget rejects atomically");
            VaultChecks.ExpectFailure(() => workspace.ImportText("test", "test", Guid.NewGuid()), "import unknown folder rejects atomically");
            VaultChecks.Require(workspace.Notes.Count == 1 && events == 0 && workspace.Capture().Notes[0].RevisionId == captured.Notes[0].RevisionId, "failed import mutated note count/basis/events");
            var reentrant = new EditingWorkspace(TimeProvider.System);
            ((System.Collections.Specialized.INotifyCollectionChanged)reentrant.Notes).CollectionChanged += (_, e) => { if(e.NewItems is not null) ((NoteDraft)e.NewItems[0]!).Title="callback edit"; };
            reentrant.ImportText("original import","body"); VaultChecks.Require(reentrant.Capture().Notes.Single().Title=="callback edit","collection notification edits must survive import basis acceptance");
            var closing = new EditingWorkspace(TimeProvider.System);
            ((System.Collections.Specialized.INotifyCollectionChanged)closing.Notes).CollectionChanged += (_, e) => { if(e.NewItems is not null) closing.Clear(); };
            closing.ImportText("must not reinject","must not reinject");
            VaultChecks.Require(closing.Notes.Count==0 && closing.FrozenBasis.Notes.Length==0,"collection notification clear must not reinject plaintext basis after close");
            string output = Path.Combine(root, "export.txt"); TextTransfer.Write(note, output);
            VaultChecks.Require(File.ReadAllText(output, new UTF8Encoding(false, true)) == imported.Title + "\r\n\r\n" + imported.Text.Replace("\n", "\r\n"), "export must retain title/body in Windows UTF8 text");
            note.Text = "bad\uD800";
            VaultChecks.ExpectFailure(() => TextTransfer.Write(note, Path.Combine(root,"invalid-utf8-output.txt")), "strict export encoder must reject unpaired surrogate before creating file");
            VaultChecks.Require(!File.Exists(Path.Combine(root,"invalid-utf8-output.txt")), "invalid export created destination"); note.Text = imported.Text;
            using (var prepared = TextTransfer.Capture(note))
            {
                VaultChecks.ExpectFailure(() => TextTransfer.WritePrepared(prepared, Path.Combine(root,"failed-output.txt"), files:new VaultFailureChecks.FaultFiles("pre-flush")), "synthetic write/flush failure");
            }
            using(var cancel=new CancellationTokenSource())
            using(var prepared=TextTransfer.Capture(note))
            {
                string cancelledPath=Path.Combine(root,"cancelled-output.txt"); bool rejected=false;
                try {TextTransfer.WritePrepared(prepared,cancelledPath,cancel.Token,new CancelOnCreateFiles(cancel));} catch(OperationCanceledException){rejected=true;}
                VaultChecks.Require(rejected && new FileInfo(cancelledPath).Length==0,"cancellation during create-new must prevent first plaintext payload write");
            }
            var before = File.ReadAllBytes(output); VaultChecks.ExpectFailure(() => TextTransfer.Write(note, output), "text export never overwrites existing destination");
            VaultChecks.Require(File.ReadAllBytes(output).SequenceEqual(before), "existing export bytes preserved");
            var linkedSource = Path.Combine(root, "linked.txt"); File.CreateSymbolicLink(linkedSource, source);
            VaultChecks.ExpectFailure(() => TextTransfer.Read(linkedSource), "linked leaf reject");
            var linkedParent = Path.Combine(root, "linked-parent"); Directory.CreateSymbolicLink(linkedParent, root);
            VaultChecks.ExpectFailure(() => TextTransfer.Read(Path.Combine(linkedParent,Path.GetFileName(source))), "linked ancestor input reject");
            VaultChecks.ExpectFailure(() => TextTransfer.Write(note, Path.Combine(linkedParent,"linked-output.txt")), "linked ancestor output reject");
            Directory.Delete(linkedParent); File.Delete(linkedSource);
            var full = new EditingWorkspace(TimeProvider.System); for(int i=0;i<100;i++) full.CreateNote(); var fullBefore=full.Capture(); full.AcceptPrepared(fullBefore); int fullEvents=0; full.Changed +=()=>fullEvents++;
            VaultChecks.ExpectFailure(()=>full.ImportText("overflow","overflow"), "import 100-note budget rejects");
            VaultChecks.Require(full.Notes.Count==100 && fullEvents==0 && full.Capture().Notes.Select(n=>n.RevisionId).SequenceEqual(fullBefore.Notes.Select(n=>n.RevisionId)), "import overflow must preserve full snapshot/events");
            workspace.Clear(); VaultChecks.ExpectFailure(() => TextTransfer.Write(note, Path.Combine(root, "locked.txt")), "closed note must not export");
            Console.WriteLine("PASS: bounded strict UTF8/BOM UTF16 import, source hash/preservation, binary/encoding/size rejection, explicit plaintext export and no-overwrite/closed guard");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class CancelOnCreateFiles(CancellationTokenSource source) : MemoApp.Core.Storage.IAtomicVaultFiles
    {
        private readonly MemoApp.Core.Storage.AtomicVaultFiles real=new();
        public Stream CreateNew(string path){var stream=real.CreateNew(path);source.Cancel();return stream;}
        public void FlushToDisk(Stream stream)=>real.FlushToDisk(stream);
        public void Replace(string temporary,string current,string previous)=>throw new InvalidOperationException();
        public void Move(string temporary,string current)=>throw new InvalidOperationException();
    }
}

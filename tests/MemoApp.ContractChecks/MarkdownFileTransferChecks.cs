using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
internal static class MarkdownFileTransferChecks
{
    private static ImportedText Read(string path,CancellationToken token=default)
    {
        var type=typeof(TextTransfer).Assembly.GetType("MemoApp.Core.Transfer.MarkdownFileTransfer");VaultChecks.Require(type is not null,"Bounded raw Markdown file adapter is missing");
        try{return (ImportedText)type!.GetMethod("Read")!.Invoke(null,[path,token])!;}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static NoteDraft Import(EditingWorkspace workspace,ImportedText text,Guid? folder=null)
    {
        var method=typeof(EditingWorkspace).GetMethod("ImportMarkdown");VaultChecks.Require(method is not null,"Atomic fresh Markdown import is missing");
        try{return (NoteDraft)method!.Invoke(workspace,[text.Title,text.Text,folder])!;}catch(TargetInvocationException e)when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    internal static async Task Run()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-markdown-file-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            string source=Path.Combine(dir,"합성 Obsidian.md"),raw="---\r\naliases: [합성]\r\n---\n# 합성 **제목**\r\n[[보존|이름]] ![[첨부.png]]\r\n<script>literal</script>\r![원본](https://invalid.example/private.png)\n%% comment %% ==text== 😀";var original=new UTF8Encoding(false,true).GetBytes(raw);File.WriteAllBytes(source,original);var input=Read(source);
            VaultChecks.Require(input.Title=="합성 Obsidian"&&input.Text==raw&&input.SourceSha256==Convert.ToHexStringLower(SHA256.HashData(original))&&File.ReadAllBytes(source).SequenceEqual(original),"Exact decoded Markdown/raw newlines/unsupported extensions/source hash and source bytes preserved");
            using(var owner=new SaveCoordinator(EncryptedVault.Create(Path.Combine(dir,"vault"),secret,secret),TimeProvider.System))
            {
                var originalNote=owner.Workspace.CreateNote();originalNote.Text="current preserved";VaultChecks.Require(await owner.SaveAsync(),"Markdown existing saved");var before=originalNote.Id;var imported=Import(owner.Workspace,input);VaultChecks.Require(imported.Id!=before&&imported.Mode=="markdown"&&imported.Text==raw&&imported.AttachmentIds.Length==0&&owner.Workspace.Notes.Single(n=>n.Id==before).Text=="current preserved","Fresh Markdown mode atomic addition no includes/image import/current overwrite");VaultChecks.Require(await owner.SaveAsync(),"Raw Markdown encrypted save");
            }
            using(var owner=new SaveCoordinator(EncryptedVault.Open(Path.Combine(dir,"vault"),secret),TimeProvider.System))VaultChecks.Require(owner.Workspace.Notes.Single(n=>n.Mode=="markdown").Text==raw,"Actual encrypted restart retains exact raw Markdown CRLF/CR/LF and extensions");
            File.WriteAllBytes(source,[0xef,0xbb,0xbf,65,13,10,66]);VaultChecks.Require(Read(source).Text=="A\r\nB","UTF8 BOM supported without newline rewrite");File.WriteAllBytes(source,[]);VaultChecks.Require(Read(source).Text.Length==0,"Empty Markdown note allowed");
            foreach(byte[] bad in new[]{new byte[]{0xff,0xfe,65,0},new byte[]{0xff,0x80},new byte[]{65,0,66},new UTF8Encoding(false,true).GetBytes(new string('x',65537)),new byte[1024*1024+1]}){File.WriteAllBytes(source,bad);VaultChecks.ExpectFailure(()=>Read(source),"UTF16/invalid UTF8/binary NUL/text/file budget refused");}
            File.WriteAllText(source,new string('x',65536),new UTF8Encoding(false,true));VaultChecks.Require(Read(source).Text.Length==65536,"Exact decoded Markdown text limit");
            foreach(string bad in new[]{Path.Combine(dir,"unknown.txt"),"file://server/unsafe.md","\\\\server\\share\\note.md","note.md:stream"})VaultChecks.ExpectFailure(()=>Read(bad),"Format/URI/network/ADS input refused");string link=Path.Combine(dir,"linked.md");File.CreateSymbolicLink(link,source);VaultChecks.ExpectFailure(()=>Read(link),"Markdown linked file refused");File.Delete(link);
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool denied=false;try{Read(source,cancel.Token);}catch(OperationCanceledException){denied=true;}VaultChecks.Require(denied,"Pre-read cancellation refuses before source access");}
            var workspace=new EditingWorkspace(TimeProvider.System);string basis=JsonSerializer.Serialize(workspace.Capture());VaultChecks.ExpectFailure(()=>Import(workspace,input,Guid.NewGuid()),"Unknown Markdown target folder rejected atomically");VaultChecks.Require(JsonSerializer.Serialize(workspace.Capture())==basis,"Unknown folder leaves exact basis unchanged");for(int i=0;i<100;i++)workspace.CreateNote();var accepted=workspace.Capture();workspace.AcceptPrepared(accepted);basis=JsonSerializer.Serialize(workspace.Capture());VaultChecks.ExpectFailure(()=>Import(workspace,input),"100-note capacity refuses import before mutation");VaultChecks.Require(JsonSerializer.Serialize(workspace.Capture())==basis,"Count refusal preserves all originals and histories");workspace.Clear();
            var now=DateTimeOffset.UtcNow;var largeNotes=Enumerable.Range(0,42).Select(i=>new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"synthetic",new string('가',65536))).ToArray();var largeSnapshot=new VaultSnapshot(4,Guid.NewGuid(),largeNotes);VaultEnvelope.Validate(largeSnapshot);var large=new EditingWorkspace(TimeProvider.System,largeSnapshot);basis=JsonSerializer.Serialize(large.Capture());VaultChecks.ExpectFailure(()=>Import(large,new ImportedText("overflow",new string('가',65536),"synthetic")),"Whole serialized payload limit rejected before new Markdown note");VaultChecks.Require(JsonSerializer.Serialize(large.Capture())==basis,"Whole payload refusal preserves complete existing raw documents/revisions/metadata");large.Clear();
            var closing=new EditingWorkspace(TimeProvider.System);((System.Collections.Specialized.INotifyCollectionChanged)closing.Notes).CollectionChanged+=(_,e)=>{if(e.NewItems is not null)closing.Clear();};Import(closing,input);VaultChecks.Require(closing.Notes.Count==0&&closing.FrozenBasis.Notes.Length==0,"Import notification close never reinjects plaintext source");
            await SaveFailure(dir+"-failure",secret,input);
            Console.WriteLine("PASS: bounded raw UTF8 Markdown adapter, exact source/newlines/extensions and source hash preservation, fresh atomic markdown mode, encrypted restart, format/binary/path/count/folder/cancellation and whole dirty save-failure preservation");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(dir))Directory.Delete(dir,true);if(Directory.Exists(dir+"-failure"))Directory.Delete(dir+"-failure",true);}
    }
    private static async Task SaveFailure(string root,byte[] secret,ImportedText input)
    {
        var files=new ToggleFiles();using var owner=new SaveCoordinator(EncryptedVault.Create(root,secret,secret,files),TimeProvider.System);var original=owner.Workspace.CreateNote();original.Text="saved";VaultChecks.Require(await owner.SaveAsync(),"Markdown flush fixture baseline");byte[] cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));original.Text="dirty preserved";var added=Import(owner.Workspace,input);files.Fail=true;VaultChecks.Require(!await owner.SaveAsync()&&owner.IsDirty&&added.Text==input.Text&&original.Text=="dirty preserved"&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(cipher),"Flush failure retains imported raw document and original dirty edit with exact old ciphertext");
    }
    private sealed class ToggleFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool Fail;public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic Markdown flush failure");actual.FlushToDisk(stream);}public void Move(string a,string b)=>actual.Move(a,b);public void Replace(string a,string b,string c)=>actual.Replace(a,b,c);
    }
}

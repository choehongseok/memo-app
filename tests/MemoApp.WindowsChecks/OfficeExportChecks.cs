using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;
internal static partial class Program
{
    private static async Task OfficeExportRun()
    {
        await WordImageOfficeExportChecks();
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-office-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(Path.Combine(root,"vault"));main.Show();Require(main.FindName("ExcelExportButton") is Button&&main.FindName("WordExportButton") is Button,"Offline Excel and Word native export commands are missing");Invoke(main,"StartSession",EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();var note=active.Workspace.CreateNote();note.Title="합성 제목";note.Text="=SUM(1,2) 한글 😀";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");var method=typeof(MainWindow).GetMethod("ExportSelectedOfficeAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var closures=typeof(MainWindow).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t=>t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Any(m=>m.Name.StartsWith("<WriteOfficeExportAsync>",StringComparison.Ordinal))).ToArray();var allowed=new[]{typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};Require(closures.Length==1&&closures[0].GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).All(f=>allowed.Contains(f.FieldType)),"Compiled Office worker excludes window/note/session/key owners");
            string denied=Path.Combine(root,"denied.xlsx");Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>{note.Text="changed in consent";return true;}),new Func<string?>(()=>throw new Exception("No stale source picker")),null])!&&!File.Exists(denied),"Consent source-version change refuses before picker");
            Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>true),new Func<string?>(()=>{active.Workspace.AcceptPrepared(active.Workspace.Capture());return denied;}),null])!&&!File.Exists(denied),"Same-version accepted-source epoch change refuses after picker");
            Require(!await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Spreadsheet,new Func<bool>(()=>true),new Func<string?>(()=>Path.Combine(root,"vault","denied.xlsx")),null])!&&!File.Exists(Path.Combine(root,"vault","denied.xlsx")),"Office plaintext output inside active vault refused");
            foreach(var format in new[]{OfficeTextFormat.Spreadsheet,OfficeTextFormat.Word})
            {
                string path=Path.Combine(root,format==OfficeTextFormat.Spreadsheet?"selected.xlsx":"selected.docx");
                Require(!await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>false),new Func<string?>(()=>throw new Exception("No Office picker without plaintext confirmation")),null])!&&!File.Exists(path),"Plaintext refusal produces no package");
                Require(await (Task<bool>)method.Invoke(main,[format,new Func<bool>(()=>true),new Func<string?>(()=>path),null])!,"Actual selected native Office export writes package");using var zip=ZipFile.OpenRead(path);Require(zip.GetEntry("[Content_Types].xml") is not null,"Generated package has content types");
            }
            active.Workspace.ConvertMode(note,"rich",true);active.Workspace.SetRichDocument(note,new(1,"""{"nodes":[{"type":"paragraph","runs":[{"text":"서식 합성 😀","bold":true,"underline":true,"fontFamily":"D2Coding","fontSize":16,"foreground":"#123456","link":"https://example.invalid/synthetic"}]},{"type":"list","ordered":true,"items":[{"runs":[{"text":"순서"}]}]},{"type":"checklist","items":[{"checked":true,"runs":[{"text":"완료"}]}]},{"type":"table","rows":[[{"runs":[{"text":"셀"}]}]]}]}"""));Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");string canonical=note.Document!.SourceJson;string richPath=Path.Combine(root,"rich-selected.docx");
            Require(await (Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,new Func<bool>(()=>true),new Func<string?>(()=>richPath),null])!,"Actual native Word command exports canonical rich source");
            using(var zip=ZipFile.OpenRead(richPath)){using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";Require(xml.Descendants(w+"b").Any()&&xml.Descendants(w+"u").Any()&&xml.Descendants(w+"numPr").Count()==1&&xml.Descendants(w+"tbl").Count()==1&&xml.Descendants(w+"t").Any(t=>t.Value=="[x] ")&&xml.Descendants(w+"t").Any(t=>t.Value.Contains("https://example.invalid/synthetic")),"Actual Word route preserves run styles/list/table/check state and inert URL");}Require(note.Document!.SourceJson==canonical,"Actual Word export preserves canonical original");
            string fresh=Path.Combine(root,"cancelled.docx");var paused=new PausedBatchFiles();var pending=(Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,new Func<bool>(()=>true),new Func<string?>(()=>fresh),paused])!;try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted,"Lock immediately releases keys while Office worker holds only prepared bytes");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(fresh).Length==0,"Late Office CreateNew cancellation writes no plaintext");await locking;Require(!Control<Button>(main,"ExcelExportButton").IsEnabled&&!Control<Button>(main,"WordExportButton").IsEnabled,"Locked Office commands disabled");}finally{paused.Continue.TrySetResult();await pending;}
        }
        finally{bool cleaned=main is null;try{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");try{if(active is not null)await active.LockAsync();}finally{if(active is null||active.IsLocked&&active.KeysReleased&&!active.IsBusy){active?.Dispose();SetField(main,"session",null!);SetField(main,"confirmedExit",true);main.Close();cleaned=true;}}}}finally{CryptographicOperations.ZeroMemory(secret);if(cleaned)Directory.Delete(root,true);}}
    }
    private static async Task WordImageOfficeExportChecks()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-word-images-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");Directory.CreateDirectory(dir);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;Task<bool>? pending=null;PausedBatchFiles? paused=null;
        try
        {
            main=new MainWindow(root);SetField(main,"searchBlocked",true);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var note=active.Workspace.CreateNote();note.Title="인라인 PNG 원본";note.Text="before\nafter";active.Workspace.ConvertMode(note,"rich",true);Require(await active.PrepareAttachmentsAsync(),"Word inline fixture has authenticated committed root");Guid image=active.AttachBytes(note,PreviewPng,"same-name.png","image/png",note.EditVersion);active.AttachBytes(note,new byte[]{1,2,3},"independent.bin","application/octet-stream",note.EditVersion);
            Require(await active.InsertInlineImageAsync(note,image,1,note.EditVersion)&&await active.InsertInlineImageAsync(note,image,3,note.EditVersion),"Actual canonical v2 repeats the same authenticated image in distinct ordered blocks");
            var other=active.Workspace.CreateNote();other.Title="선택 별도 메모";other.Text="literal <https://example.invalid>";Require(await active.SaveAsync(),"Word image native source checkpoint");
            SetField(main,"searchBlocked",false);Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");await Idle();var list=Control<ListBox>(main,"NotesList");list.UnselectAll();list.SelectedItem=note;SetField(main,"searchBlocked",true);Require(await active.SaveAsync(),"Word image native UI state settled");
            var method=typeof(MainWindow).GetMethod("ExportSelectedOfficeAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            Task<bool> Word(string name,Func<bool>? confirm=null,Func<string?>? choose=null,IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(main,[OfficeTextFormat.Word,confirm??(()=>true),choose??(()=>Path.Combine(dir,name)),files])!;
            void RoundTrip(){list.SelectedItem=other;list.SelectedItem=note;}
            Require(!await Word("no.docx",()=>false,()=>throw new Exception("No destination without default-No plaintext consent")),"Image-aware selected Word default-No refuses before picker");
            Require(!await Word("away.docx",()=>{RoundTrip();return true;},()=>throw new Exception("No destination after selection away/back")),"Word source authority subscribed before native confirmation and monotonic selection revoke");
            Require(!await Word("edit-consent.docx",()=>{note.Title="explicit consent edit";return true;},()=>throw new Exception("No destination after consent source edit"))&&note.Title=="explicit consent edit","Native Word consent edit is retained while original export authority is revoked");note.Title="인라인 PNG 원본";Require(await active.SaveAsync(),"Explicit consent edit checkpoint restores fixture title");
            Require(!await Word("accepted.docx",choose:()=>{active.Workspace.AcceptPrepared(active.Workspace.Capture());return Path.Combine(dir,"accepted.docx");})&&!File.Exists(Path.Combine(dir,"accepted.docx")),"Same-version accepted-source epoch change refuses image Word before any destination");
            Require(!await Word("inside.docx",choose:()=>Path.Combine(root,"inside.docx"))&&!File.Exists(Path.Combine(root,"inside.docx")),"Image-aware plaintext Word refuses active vault directory");
            Require(!await Word("wrong.xlsx")&&!File.Exists(Path.Combine(dir,"wrong.xlsx")),"Image-aware Word keeps exact docx extension boundary");
            byte[] snapshot=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try
            {
                Require(await Word("images.docx"),"Actual native selected rich v2 exports every authenticated inline PNG");
                using(var zip=ZipFile.OpenRead(Path.Combine(dir,"images.docx")))
                {
                    var media=zip.Entries.Where(e=>e.FullName.StartsWith("word/media/",StringComparison.Ordinal)).ToArray();Require(media.Length==1&&media[0].FullName=="word/media/image0001.png","Repeated placements share exactly one generated image media part");
                    using var imagePart=media[0].Open();using var copy=new MemoryStream();imagePart.CopyTo(copy);byte[] bytes=copy.ToArray();try{Require(bytes.SequenceEqual(PreviewPng),"Word media bytes exactly equal authenticated PNG originals without transcoding");}finally{CryptographicOperations.ZeroMemory(bytes);}
                    using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main",r="http://schemas.openxmlformats.org/officeDocument/2006/relationships",a="http://schemas.openxmlformats.org/drawingml/2006/main";
                    Require(xml.Descendants(w+"drawing").Count()==2&&xml.Descendants(a+"blip").Select(x=>(string?)x.Attribute(r+"embed")).Distinct().Count()==1,"Every actual image placement emitted once and linked to the same internal media");
                    Require(xml.Descendants(w+"t").Any(x=>x.Value.Contains("before",StringComparison.Ordinal))&&xml.Descendants(w+"t").Any(x=>x.Value.Contains("after",StringComparison.Ordinal))&&!xml.Descendants(w+"t").Any(x=>x.Value.Contains("[이미지",StringComparison.Ordinal)),"Word image blocks replace declared projection markers while retaining surrounding original text");
                    using var rel=zip.GetEntry("word/_rels/document.xml.rels")!.Open();var relationships=XDocument.Load(rel);Require(relationships.Descendants().Attributes("TargetMode").All(x=>x.Value!="External")&&zip.Entries.All(x=>!x.FullName.Contains("independent",StringComparison.Ordinal)),"Image export has no external relations or independent attachment embedding");
                }
                Require(snapshot.SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Native image Word read-only export preserves exact current/history/source/object/ciphertext");
                byte[] first=File.ReadAllBytes(Path.Combine(dir,"images.docx"));try{Require(!await Word("images.docx")&&first.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"images.docx"))),"Existing destination collision never overwrites original Word file");}finally{CryptographicOperations.ZeroMemory(first);}
                list.SelectAll();Require(await Word("mixed.docx"),"Whole ordered mixed plain and rich-v2 selected Word exports atomically");list.UnselectAll();list.SelectedItem=note;
                foreach(string mutation in new[]{"selection","accept","edit","detach"})
                {
                    paused=new();string name="blocked-"+mutation+".docx";pending=Word(name,files:paused);
                    try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));if(mutation=="selection")RoundTrip();else if(mutation=="accept")active.Workspace.AcceptPrepared(active.Workspace.Capture());else if(mutation=="edit")note.Title+=" explicit edit";else{active.RemoveInlineImage(note,3,note.EditVersion);active.RemoveInlineImage(note,1,note.EditVersion);active.DetachAttachment(note,image,note.EditVersion);}paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(Path.Combine(dir,name)).Length==0,"Word blocked CreateNew exact operation revocation writes no late plaintext: "+mutation);}
                    finally{paused.Continue.TrySetResult();await pending;pending=null;paused=null;}
                    if(mutation=="edit"){note.Title="인라인 PNG 원본";Require(await active.SaveAsync(),"Explicit edit checkpoint after refused Word write");}
                    if(mutation=="detach"){image=active.AttachBytes(note,PreviewPng,"same-name.png","image/png",note.EditVersion);Require(await active.InsertInlineImageAsync(note,image,1,note.EditVersion)&&await active.InsertInlineImageAsync(note,image,3,note.EditVersion)&&await active.SaveAsync(),"Explicitly reattached synthetic inline source checkpoint after detach refusal");}
                }
                var workerTypes=typeof(MainWindow).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t=>t.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Any(m=>m.Name.StartsWith("<WriteWordImageExportAsync>",StringComparison.Ordinal))).ToArray();var fields=new[]{typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};Require(workerTypes.Length==1&&workerTypes[0].GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).All(f=>fields.Contains(f.FieldType)),"Compiled selected Word file worker retains only prepared package/path/token/backend, never source/session/key/native owner");
                paused=new();pending=Word("locked.docx",files:paused);
                try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted,"Word plaintext file worker cannot delay key release");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(Path.Combine(dir,"locked.docx")).Length==0,"Word lock after blocked file creation never writes prepared plaintext late");await locking;}
                finally{paused.Continue.TrySetResult();await pending;pending=null;paused=null;}
            }
            finally{CryptographicOperations.ZeroMemory(snapshot);CryptographicOperations.ZeroMemory(cipher);}
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();note=active.Workspace.Notes.Single(n=>n.Title=="인라인 PNG 원본");Require(note.Document?.SchemaVersion==2,"Actual encrypted reopen retains canonical v2 source for Word export");SetField(main,"searchBlocked",false);Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");await Idle();list.UnselectAll();list.SelectedItem=note;SetField(main,"searchBlocked",true);Require(await Word("reopened.docx"),"Reopened actual authenticated source exports without root mutation or new image decoding service");
            var malformed=active.Workspace.CreateNote();malformed.Title="malformed pixel stream";active.Workspace.ConvertMode(malformed,"rich",true);byte[] bad=MalformedWordPixelPng();
            try{_=PngPreviewProfile.Inspect(bad);Guid badId=active.AttachBytes(malformed,bad,"bad.png","image/png",malformed.EditVersion);Require(await active.InsertInlineImageAsync(malformed,badId,0,malformed.EditVersion)&&await active.SaveAsync(),"CRC-valid malformed pixel stream authenticated as exact inline source before Word validation");}
            finally{CryptographicOperations.ZeroMemory(bad);}
            SetField(main,"searchBlocked",false);Invoke(main,"RefreshNotes",malformed);await Idle();await Field<Task>(main,"recentTask");await Idle();list.UnselectAll();list.SelectedItem=malformed;SetField(main,"searchBlocked",true);Require(await active.SaveAsync(),"Malformed native selection UI checkpoint");var untouchedFiles=new RecordingMergeFiles();byte[] beforeBad=File.ReadAllBytes(Path.Combine(root,"current.vault"));string badSource=malformed.Document!.SourceJson;
            try{Require(!await Word("malformed.docx",files:untouchedFiles)&&untouchedFiles.Destination is null&&!File.Exists(Path.Combine(dir,"malformed.docx"))&&malformed.Document!.SourceJson==badSource&&beforeBad.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Actual native Word whole-package refusal validates pixel semantics before CreateNew and preserves authenticated source/ciphertext");}
            finally{CryptographicOperations.ZeroMemory(beforeBad);}
            list.UnselectAll();list.SelectedItem=note;Require(await Word("post-malformed.docx"),"Failed native image build actually settles and releases one-context admission for later genuine export");Require(Directory.GetFiles(dir,"*.png",SearchOption.AllDirectories).Length==0,"Word creates no plaintext temporary PNG media files");
            paused=new();pending=Word("closed.docx",files:paused);
            try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));SetField(main,"confirmedExit",true);main.Close();Require(!pending.IsCompleted,"Actual host close leaves prepared ZIP owned through still-blocked file worker");paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(Path.Combine(dir,"closed.docx")).Length==0,"Actual closed host cancellation writes no late Word plaintext");Require(!await Word("after-close.docx",()=>throw new Exception("Closed host never confirms"),()=>throw new Exception("Closed host never picks")),"Post-close Word entry refuses before disposed file-operation CTS access");}
            finally{paused.Continue.TrySetResult();await pending;pending=null;paused=null;}
            await active.LockAsync();Require(active.KeysReleased&&!active.IsBusy,"Closed Word host writer/session really settled before disposal");active.Dispose();SetField(main,"session",null!);main=null;
        }
        finally
        {
            bool cleaned=main is null&&pending is null;
            try
            {
                paused?.Continue.TrySetResult();try{if(pending is not null)await pending;}
                finally{if(main is not null){var active=Field<SaveCoordinator?>(main,"session");try{if(active is not null)await active.LockAsync();}finally{if(active is null||active.IsLocked&&active.KeysReleased&&!active.IsBusy){active?.Dispose();SetField(main,"session",null!);SetField(main,"confirmedExit",true);main.Close();cleaned=pending is null||pending.IsCompleted;}}}}
            }
            finally{CryptographicOperations.ZeroMemory(secret);if(cleaned)Directory.Delete(dir,true);}
        }
    }
    private static byte[] MalformedWordPixelPng()
    {
        byte[] png=(byte[])PreviewPng.Clone();int length=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33,4));png[41]=0;
        uint crc=uint.MaxValue;foreach(byte value in png.AsSpan(37,length+4)){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320U:crc>>1;}
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(41+length,4),~crc);return png;
    }

}

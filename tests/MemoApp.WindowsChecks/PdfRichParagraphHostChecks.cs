using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task PdfRichParagraphHostRun()
    {
        using(var dialog=new PdfExportModeChoiceWindow(()=>true))
        {
            Require(dialog.FindName("TextModeChoice") is RadioButton{IsChecked:true}&&dialog.FindName("VisualModeChoice") is RadioButton{IsChecked:false},"Actual native PDF choice retains Text as its default");
            var panel=(StackPanel)dialog.Content;
            string guidance=string.Join(" ",panel.Children.OfType<TextBlock>().Select(block=>block.Text));
            Require(new[]{"plain","rich v1","문단","굵게","밑줄","취소선","글꼴","크기","색","목록","표","v2","Markdown","선택 전체","96 DPI","OCR"}.All(guidance.Contains),"Actual mode dialog explains the closed rich paragraph profile, whole-selection refusal and raster limits");
        }
        RichHostWorkerGraph();
        await using(var fixture=await RichHostFixture.Create())
        {
            await RichHostSuccessAndRefusal(fixture);
            await RichHostCallbacks(fixture);
            await RichHostYieldAndWrite(fixture);
        }
        await RichHostTerminalWrite(false);
        await RichHostTerminalWrite(true);
    }

    private static async Task RichHostSuccessAndRefusal(RichHostFixture f)
    {
        await f.Select(f.Rich);byte[] source=PdfAuthorityStableSource(f.Active.Workspace),cipher=File.ReadAllBytes(f.CipherPath);
        try
        {
            var order=new List<string>();var files=new RichHostFiles();
            Require(await f.Mode("rich.pdf",()=>{order.Add("mode");return PdfExportMode.Visual;},()=>{order.Add("visual-consent");return true;},()=>{order.Add("picker");return f.PathFor("rich.pdf");},files),"Actual mode route exports canonical rich v1 paragraphs");
            Require(order.SequenceEqual(new[]{"mode","visual-consent","picker"})&&files.Creates==1,"Mode, explicit visual consent and external picker occur once in order before one CreateNew");
            Require(f.Rich.Document!.SourceJson==RichHostFixture.Raw, "Successful export retains exact original rich JSON");
            f.AssertStable(source,cipher,"successful rich export");
            byte[] richPdf=File.ReadAllBytes(f.PathFor("rich.pdf"));
            try
            {
                Require(!await f.Export("rich.pdf")&&File.ReadAllBytes(f.PathFor("rich.pdf")).SequenceEqual(richPdf),"Rich host preserves an existing destination through CreateNew semantics");
                await f.Select(f.Plain,f.Rich);Require(await f.Export("mixed.pdf"),"One actual visual document supports the ordered mixed selection");
                byte[] mixedPdf=File.ReadAllBytes(f.PathFor("mixed.pdf")),richRgb=VisualPdfFirstRgb(richPdf),mixedRich=VisualPdfFirstRgb(mixedPdf,1);
                using var plain=await PdfVisualRenderer.RenderPlainAsync([new(f.Plain.Title,f.Plain.Text)],()=>true,CancellationToken.None);
                byte[] plainRgb=VisualPdfFirstRgb(plain.Bytes),mixedPlain=VisualPdfFirstRgb(mixedPdf);
                try{Require(richRgb.SequenceEqual(mixedRich)&&plainRgb.SequenceEqual(mixedPlain),"Mixed file preserves selected order, exact single rich page and the existing plain pixel layout");}
                finally{foreach(var bytes in new[]{mixedPdf,richRgb,mixedRich,plainRgb,mixedPlain})CryptographicOperations.ZeroMemory(bytes);}
            }
            finally{CryptographicOperations.ZeroMemory(richPdf);}
        }
        finally{CryptographicOperations.ZeroMemory(source);CryptographicOperations.ZeroMemory(cipher);}

        foreach(var value in new (string Name,StyledDocument? Document)[]{
            ("list",new(1,"{\"nodes\":[{\"type\":\"list\",\"ordered\":false,\"items\":[{\"runs\":[{\"text\":\"last\"}]}]}]}")),
            ("checklist",new(1,"{\"nodes\":[{\"type\":\"checklist\",\"items\":[{\"checked\":true,\"runs\":[{\"text\":\"last\"}]}]}]}")),
            ("table",new(1,"{\"nodes\":[{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"last\"}]}]]}]}")),
            ("link",RichHostDocument(new{text="last",link="https://synthetic.invalid"})),
            ("family",RichHostDocument(new{text="last",fontFamily="Arial"})),
            ("markdown",null)})
        {
            f.Active.Workspace.ConvertMode(f.Last,value.Document is null?"markdown":"rich",true);
            if(value.Document is not null)f.Active.Workspace.SetRichDocument(f.Last,value.Document);
            await f.Select(f.Rich,f.Last);source=PdfAuthorityStableSource(f.Active.Workspace);cipher=File.ReadAllBytes(f.CipherPath);var files=new RichHostFiles();
            try
            {
                Require(!await f.Export("unsupported-"+value.Name+".pdf",files:files)&&files.Creates==0&&!File.Exists(f.PathFor("unsupported-"+value.Name+".pdf")),"Valid first rich note plus unsupported last "+value.Name+" refuses the whole selection before CreateNew");
                f.AssertStable(source,cipher,"unsupported last "+value.Name);
            }
            finally{CryptographicOperations.ZeroMemory(source);CryptographicOperations.ZeroMemory(cipher);}
        }
        f.Active.Workspace.ConvertMode(f.Last,"rich",true);
        foreach(var value in new[]{("native-face",RichHostDocument(new{text="ABC",fontFamily="Segoe UI Symbol",bold=true})),("page-overflow",RichHostDocument(new{text=new string('\n',42*256)}))})
        {
            f.Active.Workspace.SetRichDocument(f.Last,value.Item2);await f.Select(f.Rich,f.Last);var files=new RichHostFiles();
            source=PdfAuthorityStableSource(f.Active.Workspace);cipher=File.ReadAllBytes(f.CipherPath);
            try
            {
                Require(!await f.Export(value.Item1+".pdf",files:files)&&files.Creates==0,"Late native face refusal or page overflow never creates a partial selected PDF: "+value.Item1);
                f.AssertStable(source,cipher,value.Item1);
            }
            finally{CryptographicOperations.ZeroMemory(source);CryptographicOperations.ZeroMemory(cipher);}
        }
    }

    private static async Task RichHostCallbacks(RichHostFixture f)
    {
        await f.Select(f.Rich);var files=new RichHostFiles();int consent=0,picker=0;
        Require(!await f.Mode("cancel-mode.pdf",()=>null,()=>{consent++;return true;},()=>{picker++;return f.PathFor("cancel-mode.pdf");},files)&&consent==0&&picker==0&&files.Creates==0,"Canceled mode requests no plaintext consent, picker or file");
        Require(!await f.Export("declined.pdf",()=>false,()=>{picker++;return f.PathFor("declined.pdf");},files)&&picker==0&&files.Creates==0,"Declined explicit visual consent never opens a picker or file");
        Require(!await f.Export("picker-cancel.pdf",choose:()=>null,files:files)&&files.Creates==0,"Canceled picker creates no file");
        Require(!await f.Export("inside.pdf",choose:()=>Path.Combine(f.Root,"inside.pdf"),files:files)&&files.Creates==0,"Rich PDF refuses an active vault destination before CreateNew");
        Require(!await f.Export("wrong.txt",files:files)&&files.Creates==0,"Rich PDF requires an external .pdf path");
        foreach(string boundary in new[]{"mode","consent","picker"})
        {
            await f.Select(f.Rich);files=new();consent=picker=0;
            var result=await f.Mode(boundary+"-roundtrip.pdf",()=>{if(boundary=="mode")f.RoundTrip();return PdfExportMode.Visual;},()=>{consent++;if(boundary=="consent")f.RoundTrip();return true;},()=>{picker++;if(boundary=="picker")f.RoundTrip();return f.PathFor(boundary+"-roundtrip.pdf");},files);
            Require(!result&&files.Creates==0&&(boundary!="mode"||consent==0)&&(boundary=="picker"||picker==0),"Original authority permanently revokes a selection round trip at "+boundary);
        }
        await f.Select(f.Rich);var button=Control<Button>(f.Main,"PdfExportButton");var descriptor=DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool nativeEntered=false;Task<bool>? nested=null;files=new();consent=0;
        EventHandler changed=(_,_)=>{if(nativeEntered||button.IsEnabled)return;nativeEntered=true;nested=f.Export("native-reentry.pdf",()=>throw new Exception("Busy native reentry must not ask consent"));f.RoundTrip();};
        descriptor.AddValueChanged(button,changed);
        try{Require(!await f.Export("native-roundtrip.pdf",()=>{consent++;return true;},files:files)&&nativeEntered&&nested is not null&&!await nested&&consent==0&&files.Creates==0,"Actual native IsEnabled callback cannot reenter admission and selection round trip revokes before consent");}
        finally{descriptor.RemoveValueChanged(button,changed);}
        foreach(string callback in new[]{"consent","picker"})
        {
            await f.Select(f.Rich);files=new();
            Require(!await f.Export(callback+"-throws.pdf",callback=="consent"?()=>throw new InvalidOperationException("Synthetic consent callback"):()=>true,()=>throw new InvalidOperationException("Synthetic picker callback"),files)&&files.Creates==0&&!Field<bool>(f.Main,"pdfExportBusy"),"Callback failure settles admission without CreateNew: "+callback);
        }
    }

    private static async Task RichHostYieldAndWrite(RichHostFixture f)
    {
        foreach(string mutation in new[]{"edit","same-version-accept","selection-roundtrip"})
        {
            f.Active.Workspace.SetRichDocument(f.Rich,RichHostDocument(new{text=string.Join('\n',Enumerable.Repeat("합성 긴 ABC",100))}));await f.Select(f.Rich);var files=new RichHostFiles();
            var pending=f.Export("yield-"+mutation+".pdf",files:files);Require(!pending.IsCompleted&&Field<bool>(f.Main,"pdfExportBusy"),"Native rich preparation reaches an actual bounded dispatcher yield");
            f.Mutate(mutation);Require(!await f.Export("yield-reentry.pdf",()=>throw new Exception("Unsettled native preparation retains admission")),"Canceled preparation retains busy admission until its real task settles");
            Require(!await pending&&files.Creates==0&&!Field<bool>(f.Main,"pdfExportBusy"),"Yield mutation discards the candidate before file creation: "+mutation);
        }
        f.Active.Workspace.SetRichDocument(f.Rich,new(1,RichHostFixture.Raw));
        foreach(string mutation in new[]{"none","edit","same-version-accept","selection-roundtrip"})
        {
            await f.Select(f.Rich);var files=new RichHostFiles(true);var pending=f.Export("write-"+mutation+".pdf",files:files);PreparedTextExport? prepared=null;byte[]? bytes=null,source=null,cipher=null;
            try
            {
                await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));prepared=RichHostPrepared(pending);bytes=prepared.Bytes;
                Require(!prepared.IsDisposed&&bytes.Length>0&&bytes.Any(value=>value!=0)&&Field<bool>(f.Main,"pdfExportBusy")&&!pending.IsCompleted,"Real blocked CreateNew retains live complete prepared bytes and occupied admission");
                if(mutation!="none")f.Mutate(mutation);
                source=PdfAuthorityStableSource(f.Active.Workspace);cipher=File.ReadAllBytes(f.CipherPath);
                Require(!await f.Export("blocked-reentry.pdf",()=>throw new Exception("Actual file worker has not settled")),"A canceled or live actual file worker prevents busy-slot reuse");
                Require(!prepared.IsDisposed&&bytes.Any(value=>value!=0),"Cancellation request alone does not dispose a worker-owned prepared payload");
                files.Continue.TrySetResult();bool accepted=await pending;
                Require(accepted==(mutation=="none")&&new FileInfo(f.PathFor("write-"+mutation+".pdf")).Length is var length&&(mutation=="none"?length>0:length==0),"Late CreateNew writes only while its original authority remains current: "+mutation);
                Require(prepared.IsDisposed&&bytes.All(value=>value==0)&&!Field<bool>(f.Main,"pdfExportBusy"),"Prepared bytes zero and admission releases only after actual file task settlement: "+mutation);
                f.AssertStable(source,cipher,"actual worker settlement after "+mutation);
            }
            finally{files.Continue.TrySetResult();await pending;if(source is not null)CryptographicOperations.ZeroMemory(source);if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);}
        }
    }

    private static async Task RichHostTerminalWrite(bool close)
    {
        await using var f=await RichHostFixture.Create();await f.Select(f.Rich);var files=new RichHostFiles(true);var pending=f.Export(close?"closed.pdf":"locked.pdf",files:files);
        try
        {
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));var prepared=RichHostPrepared(pending);var bytes=prepared.Bytes;Task<bool>? locking=null;
            if(close){SetField(f.Main,"confirmedExit",true);f.Main.Close();}
            else{locking=f.Active.LockAsync();Require(f.Active.IsLocked&&f.Active.KeysReleased,"Lock releases session keys immediately while actual PDF CreateNew remains blocked");}
            Require(!pending.IsCompleted&&Field<bool>(f.Main,"pdfExportBusy")&&!prepared.IsDisposed&&bytes.Any(value=>value!=0),"Lock/close cancellation keeps the prepared payload live until actual worker completion");
            Require(!await f.Export("terminal-reentry.pdf",()=>throw new Exception("Terminal host cannot ask consent")),"Lock/close rejects command reentry before callback access");
            files.Continue.TrySetResult();Require(!await pending&&new FileInfo(f.PathFor(close?"closed.pdf":"locked.pdf")).Length==0,"CreateNew returning after lock/close writes no plaintext");
            if(locking is not null)await locking;
            Require(prepared.IsDisposed&&bytes.All(value=>value==0)&&!Field<bool>(f.Main,"pdfExportBusy"),"Terminal actual worker settles before payload zero and busy release");
        }
        finally{files.Continue.TrySetResult();await pending;}
    }

    private static StyledDocument RichHostDocument(params object[] runs)=>new(1,JsonSerializer.Serialize(new{nodes=new[]{new{type="paragraph",runs}}}));

    private static void RichHostWorkerGraph()
    {
        var workers=typeof(MainWindow).GetNestedTypes(BindingFlags.NonPublic).Where(type=>type.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(method=>method.Name.StartsWith("<WriteVisualPdfExportAsync>",StringComparison.Ordinal))).ToArray();
        Type[] allowed=[typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)];
        Require(workers.Length==1&&workers[0].GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).All(field=>allowed.Contains(field.FieldType)),"Compiled file-worker closure contains only prepared bytes, path, cancellation token and backend; no UI/source/session/key or current callback");
    }

    private static PreparedTextExport RichHostPrepared(Task task)
    {
        // Inspect only compiler task/state-machine/awaiter fields, never traverse the MainWindow or session graph.
        var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);var found=new HashSet<PreparedTextExport>();
        void Visit(object? value,int depth)
        {
            if(value is null||depth>12||!visited.Add(value))return;
            if(value is PreparedTextExport prepared){found.Add(prepared);return;}
            Type type=value.GetType();if(value is not Task&&value is not IAsyncStateMachine&&!type.Name.StartsWith("TaskAwaiter",StringComparison.Ordinal))return;
            for(Type? current=type;current is not null&&current!=typeof(object);current=current.BaseType)
                foreach(var field in current.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))Visit(field.GetValue(value),depth+1);
        }
        Visit(task,0);Require(found.Count==1,"Blocked real export task retains exactly one complete prepared PDF in its async ownership chain");return found.Single();
    }

    private sealed class RichHostFiles(bool pause=false):IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();private int creates;
        internal int Creates=>Volatile.Read(ref creates);
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path)
        {
            Interlocked.Increment(ref creates);var stream=actual.CreateNew(path);Entered.TrySetResult();
            if(pause&&!Continue.Task.Wait(TimeSpan.FromSeconds(30))){stream.Dispose();throw new IOException("Synthetic rich host CreateNew pause timed out");}return stream;
        }
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);
        public void Move(string temporary,string current)=>throw new NotSupportedException();
        public void Replace(string temporary,string current,string previous)=>throw new NotSupportedException();
    }

    private sealed class RichHostFixture:IAsyncDisposable
    {
        internal const string Raw="{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"합성 굵은 ABC\",\"bold\":true,\"underline\":true,\"strike\":true,\"fontFamily\":\"Malgun Gothic\",\"foreground\":\"#CC0000\",\"background\":\"#80FFFF00\"}]}]}";
        private readonly byte[] secret=EncryptedVault.GenerateRecoverySecret();
        private readonly string directory=Path.Combine(Path.GetTempPath(),"memo-wpf-rich-host-"+Guid.NewGuid().ToString("N"));
        internal string Root=>Path.Combine(directory,"vault");internal string CipherPath=>Path.Combine(Root,"current.vault");
        internal MainWindow Main=null!;internal SaveCoordinator Active=null!;internal NoteDraft Rich=null!,Plain=null!,Last=null!;
        private ListBox Selection=>Control<ListBox>(Main,"NotesList");
        internal string PathFor(string name)=>Path.Combine(directory,name);
        internal static async Task<RichHostFixture> Create()
        {
            var f=new RichHostFixture();Directory.CreateDirectory(f.directory);
            try
            {
                f.Main=new(f.Root);f.Main.Show();Invoke(f.Main,"StartSession",EncryptedVault.Create(f.Root,f.secret,f.secret));f.Active=Field<SaveCoordinator>(f.Main,"session");Field<DispatcherTimer>(f.Main,"timer").Stop();
                f.Rich=f.Active.Workspace.CreateNote();f.Rich.Title="";f.Active.Workspace.ConvertMode(f.Rich,"rich",true);f.Active.Workspace.SetRichDocument(f.Rich,new(1,Raw));
                f.Plain=f.Active.Workspace.CreateNote();f.Plain.Title="plain";f.Plain.Text="Existing plain layout é ABC";f.Last=f.Active.Workspace.CreateNote();f.Last.Title="last";f.Last.Text="last";
                Invoke(f.Main,"RefreshNotes",f.Rich);await f.Select(f.Rich);Require(await f.Active.SaveAsync(),"Synthetic rich host baseline is encrypted and settled");await Idle();await Field<Task>(f.Main,"recentTask");await Idle();return f;
            }
            catch{await f.DisposeAsync();throw;}
        }
        internal async Task Select(params NoteDraft[] notes)
        {
            Selection.UnselectAll();foreach(var note in notes)Selection.SelectedItems.Add(note);await Idle();await Field<Task>(Main,"recentTask");await Idle();
            Require(Selection.SelectedItems.Cast<NoteDraft>().SequenceEqual(notes),"Native fixture settles the exact ordered live selection before export");
        }
        internal Task<bool> Export(string name,Func<bool>? confirm=null,Func<string?>? choose=null,IAtomicVaultFiles? files=null)=>(Task<bool>)typeof(MainWindow).GetMethod("ExportSelectedVisualPdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Main,[confirm??(()=>true),choose??(()=>PathFor(name)),files])!;
        internal Task<bool> Mode(string name,Func<PdfExportMode?> mode,Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)=>(Task<bool>)typeof(MainWindow).GetMethod("ExportSelectedPdfWithModeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Main,[mode,new Func<bool>(()=>throw new Exception("Visual route cannot request Text consent")),confirm,choose,files])!;
        internal void RoundTrip(){Selection.SelectedItem=Plain;Selection.SelectedItem=Rich;}
        internal void Mutate(string mutation)
        {
            if(mutation=="edit")Rich.Title+="x";
            else if(mutation=="selection-roundtrip")RoundTrip();
            else
            {
                long version=Rich.EditVersion,epoch=Active.AttachmentPreviewEpoch;Active.Workspace.AcceptPrepared(Active.Workspace.Capture());
                Require(Rich.EditVersion==version&&Active.AttachmentPreviewEpoch!=epoch,"Actual same-version AcceptPrepared advances the original source epoch");
            }
        }
        internal void AssertStable(byte[] source,byte[] cipher,string scenario)
        {
            byte[] afterSource=PdfAuthorityStableSource(Active.Workspace),afterCipher=File.ReadAllBytes(CipherPath);
            try{Require(source.SequenceEqual(afterSource)&&cipher.SequenceEqual(afterCipher),"Exact source, metadata, revision/history and ciphertext remain unchanged: "+scenario);}
            finally{CryptographicOperations.ZeroMemory(afterSource);CryptographicOperations.ZeroMemory(afterCipher);}
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if(Active is not null)
                {
                    if(Field<bool>(Main,"windowClosed"))Active.Conceal-=(Action)typeof(MainWindow).GetMethod("ConcealViews",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate(typeof(Action),Main);
                    if(!Active.IsLocked)await Active.LockAsync();await Active.WhenAttachmentReadsIdle;
                    Require(Active.KeysReleased&&!Active.IsBusy,"Synthetic rich host cleanup waits for session settlement and released keys");Invoke(Main,"ReleaseSettledSession");
                }
                if(Main is not null){SetField(Main,"confirmedExit",true);Main.Close();}
            }
            finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}

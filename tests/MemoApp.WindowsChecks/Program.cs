using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;
internal static partial class Program
{
    [STAThread]
    private static int Main()
    {
        var arguments=Environment.GetCommandLineArgs();
        if(arguments.Length==3&&arguments[1]=="--installed-package")return InstalledPackageRun(arguments[2]);
        if(arguments.Contains("--image-shutdown-worker"))return ImageShutdownWorker();
        int result = 1; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                var failures=new List<string>();
                var groups=new (string Name,Func<Task> Run)[]{("rich-paste-race",RichPasteRaceRun),("rich-own-commit-race",RichCommitRaceRun),("rich-rebuild-race",RichRebuildRaceRun),("rich-post-handler-race",RichPostHandlerRaceRun),("rich-exception-purge",RichExceptionPurgeRun),("rich-clear-exception",RichClearExceptionRun),("rich-url-clear-exception",()=>RichClearExceptionCase(true)),("rich-two-views",RichViewsRun),("rich-formatting-commands",RichFormattingCommandsRun),("rich-fidelity",RichFidelityRun),("rich-link-split",RichLinkSplitRun),("rich-history-mode",RichHistoryRun),("rich-native-style-refusal",RichNativeStyleRun),("rich-native-malformed-unicode",RichMalformedNativeRun),("rich-composition-boundary",RichCompositionRun),("rich-production-integration",RichWindowsIntegrationRun),("plain-editing",Run),("batch-refusal",BatchFailureRun),("device-native",DeviceWindowsRun),("rich-nested-native-worker",RichNestedNativeProcessRun)};
                groups=groups.Append(("rich-explicit-link",(Func<Task>)RichLinkRun)).ToArray();
                groups=groups.Append(("rich-checklist-enter",(Func<Task>)ChecklistEditingRun)).ToArray();
                groups=groups.Append(("rich-checklist-enter-boundary",(Func<Task>)ChecklistEnterBoundaryRun)).ToArray();
                groups=groups.Append(("rich-composition-metadata",(Func<Task>)RichCompositionMetadataRun)).ToArray();
                groups=groups.Append(("rich-same-content-restore",(Func<Task>)RichSameContentRestoreRun)).ToArray();
                groups=groups.Append(("markdown-preview",(Func<Task>)MarkdownPreviewRun)).Append(("markdown-production",(Func<Task>)MarkdownProductionRun)).ToArray();
                groups=groups.Append(("markdown-slow-delivery",(Func<Task>)MarkdownSlowDeliveryRun)).ToArray();
                groups=groups.Append(("rich-own-same-restore",(Func<Task>)RichOwnSameRestoreRun)).ToArray();
                groups=groups.Append(("rich-same-turn-mode",(Func<Task>)(()=>EditorSameTurnModeRun("rich")))).Append(("markdown-same-turn-mode",(Func<Task>)(()=>EditorSameTurnModeRun("markdown")))).ToArray();
                groups=groups.Append(("attachment-panel-boundaries",(Func<Task>)AttachmentPanelRun)).Append(("attachment-production",(Func<Task>)AttachmentProductionRun)).ToArray();
                groups=groups.Append(("raw-markdown-file-import",(Func<Task>)MarkdownImportRun)).Append(("tray-session-lifecycle",(Func<Task>)TrayRun)).Append(("startup-registration",(Func<Task>)StartupRegistrationRun)).ToArray();
                groups=groups.Append(("file-path-link-authority",(Func<Task>)FilePathLinkRun)).ToArray();
                groups=groups.Append(("attachment-explicit-open",(Func<Task>)AttachmentOpenRun)).ToArray();
                groups=groups.Append(("selected-backup-copy",(Func<Task>)SelectedBackupRun)).ToArray();
                groups=groups.Append(("encrypted-backup-preview",(Func<Task>)BackupPreviewRun)).ToArray();
                groups=groups.Append(("backup-same-id-merge",(Func<Task>)BackupMergeUiRun)).ToArray();
                groups=groups.Append(("backup-branch-resolution",(Func<Task>)BranchResolutionUiRun)).ToArray();
                groups=groups.Append(("clipboard-png-input",(Func<Task>)ClipboardPngRun)).ToArray();
                groups=groups.Append(("png-scalar-metadata",(Func<Task>)PngMetadataRun)).ToArray();
                groups=groups.Append(("png-gray-shared-display",(Func<Task>)PngGrayRun)).ToArray();
                groups=groups.Append(("clipboard-dib-input",(Func<Task>)DibClipboardRun)).ToArray();
                groups=groups.Append(("excel-text-import",(Func<Task>)ExcelImportRun)).ToArray();
                groups=groups.Append(("excel-sheet-column-mapping",(Func<Task>)ExcelMappingRun)).ToArray();
                groups=groups.Append(("pdf-plaintext-export",(Func<Task>)PdfExportRun)).ToArray();
                groups=groups.Append(("pdf-export-authority",(Func<Task>)PdfExportAuthorityRun)).ToArray();
                groups=groups.Append(("pdf-visual-export",(Func<Task>)PdfVisualRun)).ToArray();
                groups=groups.Append(("office-plaintext-export",(Func<Task>)OfficeExportRun)).ToArray();
                groups=groups.Append(("whole-encrypted-transfer",(Func<Task>)WholeTransferRun)).ToArray();
                groups=groups.Append(("automatic-encrypted-trash",(Func<Task>)AutomaticTrashRun)).ToArray();
                groups=groups.Append(("automatic-encrypted-backup",(Func<Task>)AutomaticBackupRun)).ToArray();
                groups=groups.Append(("automatic-backup-root-binding",(Func<Task>)AutomaticBackupRootBindingRun)).ToArray();
                groups=groups.Append(("automatic-backup-persistence",(Func<Task>)AutomaticBackupPersistenceRun)).ToArray();
                groups=groups.Append(("rich-checklist-enter-completion",(Func<Task>)ChecklistEnterCompletionRun)).ToArray();
                groups=groups.Append(("manual-encrypted-backup",(Func<Task>)ManualBackupRun)).ToArray();
                groups=groups.Append(("device-search-state",(Func<Task>)DeviceSearchRun)).ToArray();
                groups=groups.Append(("batch-text-export",(Func<Task>)BatchTextExportRun)).ToArray();
                groups=groups.Append(("image-file-drop",(Func<Task>)ImageFileDropRun)).ToArray();
                groups=groups.Append(("search-match-preview",(Func<Task>)SearchMatchRun)).ToArray();
                groups=groups.Append(("image-selected-png",(Func<Task>)ImagePreviewRun)).Append(("image-paused-races",(Func<Task>)ImagePreviewRacesRun)).Append(("image-commit-fault",(Func<Task>)ImagePreviewFaultRun)).Append(("image-dispatcher-shutdown",(Func<Task>)ImageShutdownRun)).Append(("explicit-full-exit",(Func<Task>)ExitMenuRun)).Append(("trial-installer",(Func<Task>)TrialInstallerRun)).ToArray();
                groups=groups.Append(("ocr-linked-runtime",(Func<Task>)LinkedOcrRuntimeRun)).ToArray();
                groups=groups.Append(("attachment-linked-ocr-ui",(Func<Task>)AttachmentLinkedOcrUiRun)).ToArray();
                groups=groups.Append(("ocr-product-native",(Func<Task>)OcrProductRun)).Append(("ocr-shared-settlement",(Func<Task>)OcrSettlementUiRun)).ToArray();
                groups=groups.Append(("rich-image-document",(Func<Task>)RichImageDocumentRun)).Append(("rich-image-publication",(Func<Task>)RichImagePublicationRun)).ToArray();
                groups=groups.Append(("rich-image-product",(Func<Task>)RichImageProductRun)).ToArray();
                if(arguments.Contains("--h01-image-only"))groups=groups.Where(group=>group.Name is "rich-image-document" or "rich-image-publication" or "rich-image-product").ToArray();
                if(arguments.Contains("--backup-merge-only"))groups=groups.Where(group=>group.Name=="backup-same-id-merge").ToArray();
                if(arguments.Contains("--branch-resolution-only"))groups=groups.Where(group=>group.Name=="backup-branch-resolution").ToArray();
                if(arguments.Contains("--ocr-linked-only"))groups=groups.Where(group=>group.Name=="ocr-linked-runtime").ToArray();
                if(arguments.Contains("--attachment-linked-ocr-only"))groups=groups.Where(group=>group.Name=="attachment-linked-ocr-ui").ToArray();
                if(arguments.Contains("--pdf-visual-only"))groups=groups.Where(group=>group.Name=="pdf-visual-export").ToArray();
                if(arguments.Contains("--clipboard-dib-only"))groups=groups.Where(group=>group.Name=="clipboard-dib-input").ToArray();
                if(Environment.GetCommandLineArgs().Contains("--nested-native-worker"))groups=[("nested-native-isolated",RichNestedNativeWorker)];
                foreach(var group in groups)
                {
                    try{await group.Run();Console.WriteLine("PASS: WPF group "+group.Name);}
                    catch(Exception e){var actual=e.GetBaseException();string failure=group.Name+" "+actual.GetType().Name+": "+actual.Message;failures.Add(failure);Console.Error.WriteLine("FAIL: WPF synthetic checks "+failure);if(group.Name is "trial-installer" or "clipboard-dib-input" or "pdf-visual-export"){string stack=e.ToString();Console.Error.WriteLine(stack[..Math.Min(stack.Length,8192)]);}}
                    finally{foreach(var window in app.Windows.Cast<Window>().Where(w=>w.Content is StructuredNoteEditor or MarkdownNotePreview).ToArray()){((IDisposable)window.Content).Dispose();window.Close();}}
                }
                if(failures.Count==0){Console.WriteLine("PASS: actual Windows WPF rich/shared editing plus bound editing/search/organization/batch/comparison/lock clearing and native device-layout/preferences/widget/open-intent regression (not IME/physical mixed-DPI/OS SessionLock/user usability)");result=0;}
            }
            catch (Exception e) { var actual = e.GetBaseException(); Console.Error.WriteLine("FAIL: WPF synthetic checks " + actual.GetType().Name + ": " + actual.Message); }
            finally { app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static string SyntheticRenderDiagnostic(StyledDocument source)
    {
        using var json=JsonDocument.Parse(source.SourceJson);var document=new FlowDocument{FontFamily=new FontFamily("Segoe UI"),FontSize=14,PagePadding=new(0),Foreground=Brushes.Black};
        foreach(var node in json.RootElement.GetProperty("nodes").EnumerateArray())document.Blocks.Add((Block)typeof(StructuredNoteEditor).GetMethod("RenderBlock",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[node])!);
        return ((StyledDocument)typeof(StructuredNoteEditor).GetMethod("CaptureNativeDocument",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[document])!).SourceJson;
    }
    private sealed class DelayedTextData(Action access):IDataObject
    {
        public object GetData(string format,bool autoConvert){access();return "LATE_SYNTHETIC_PLAINTEXT";}
        public object GetData(string format)=>GetData(format,false);public object GetData(Type format)=>GetData(format.FullName!,false);
        public bool GetDataPresent(string format,bool autoConvert)=>format==DataFormats.UnicodeText&&!autoConvert;
        public bool GetDataPresent(string format)=>GetDataPresent(format,false);public bool GetDataPresent(Type format)=>false;
        public string[] GetFormats(bool autoConvert)=>[DataFormats.UnicodeText];public string[] GetFormats()=>GetFormats(false);
        public void SetData(string format,object data,bool autoConvert)=>throw new NotSupportedException();public void SetData(string format,object data)=>throw new NotSupportedException();public void SetData(Type format,object data)=>throw new NotSupportedException();public void SetData(object data)=>throw new NotSupportedException();
    }
    private static async Task RichPasteRaceRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="race baseline";workspace.ConvertMode(note,"rich",true);string before=note.Document!.SourceJson;bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();Require(!view.RichInput.IsReadOnly,"minimal rich preflight unexpectedly read-only; synthetic original="+note.Document!.SourceJson+" native="+SyntheticRenderDiagnostic(note.Document));
        view.PasteData(new DelayedTextData(()=>{live=false;view.ClearSensitive();}));await Idle();Require(view.RichInput.Document.Blocks.Count==0 && !view.RichInput.CanUndo && note.Document!.SourceJson==before,"delayed OLE getter cannot reinsert plaintext into concealed/disposed view");window.Close();workspace.Clear();
        workspace=new EditingWorkspace(TimeProvider.System);note=workspace.CreateNote();workspace.ConvertMode(note,"rich",true);live=true;using var changedView=new StructuredNoteEditor(workspace,note,()=>live,_=>{});window=new Window{Content=changedView,Width=400,Height=300};window.Show();await Idle();changedView.PasteData(new DelayedTextData(()=>workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("CONCURRENT_CANONICAL_CHANGE"))));await Idle();Require(note.Text=="CONCURRENT_CANONICAL_CHANGE","delayed OLE getter must not paste into newer canonical source/version");live=false;changedView.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichRebuildRaceRun()
    {
        var errors=new List<string>();
        foreach(bool conceal in new[]{true,false})
        {
            var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="REBUILD_A";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;
            view.RichInput.TextChanged+=(_,_)=>{if(armed){armed=false;if(conceal){live=false;view.ClearSensitive();}else workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("REBUILD_B"));}};
            try
            {
                Invoke(view,"Rebuild");await Idle();
                Require(conceal?view.RichInput.Document.Blocks.Count==0&&!view.RichInput.CanUndo:new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("REBUILD_B")&&!view.RichInput.CanUndo,"rebuild publish callback must never reattach stale A after conceal/latest B");
            }
            catch(Exception error){errors.Add((conceal?"conceal: ":"source B: ")+error.GetBaseException().Message);}
            finally{live=false;view.ClearSensitive();workspace.Clear();window.Close();}
        }
        Require(errors.Count==0,string.Join(" / ",errors));
    }
    private static async Task RichPostHandlerRaceRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="ordinary before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;
        view.RichInput.TextChanged+=(_,_)=>{if(armed){armed=false;workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("POST_HANDLER_B"));}};
        ((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.Add(new Run("_A"));await Idle();Require(note.Text=="POST_HANDLER_B" && new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("POST_HANDLER_B") && !view.RichInput.CanUndo,"external routed TextChanged handler after own handler must defer latest source publish until native event unwinds");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichNestedNativeProcessRun()
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--nested-native-worker");
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}catch{if(!child.HasExited)child.Kill(true);throw new Exception("isolated synthetic nested native worker timed out");}string output=await stdout+await stderr;Require(child.ExitCode==0,"isolated nested native publish worker exit="+child.ExitCode+" "+string.Join(" / ",output.Split('\n').Where(l=>l.Contains("FAIL:")||l.Contains("Process terminated")||l.Contains("Unrecoverable"))));
    }
    private static async Task RichNestedNativeWorker()
    {
        int requestedPumps=0,refusedPumps=0;
        void Pump(){requestedPumps++;var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));try{Dispatcher.PushFrame(frame);}catch(InvalidOperationException e) when(e.Message.Contains("dispatcher processing is suspended",StringComparison.OrdinalIgnoreCase)){refusedPumps++;}}
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="nested before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;
        view.RichInput.TextChanged+=(_,_)=>{if(armed){armed=false;workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("NESTED_HANDLER_B"));Pump();}};
        ((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.Add(new Run("_A"));await Idle();Require(note.Text=="NESTED_HANDLER_B" && new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("NESTED_HANDLER_B"),"nested dispatcher pump must not replace Document inside outer native TextChanged");
        view.RichInput.BeginChange();try{workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("OPEN_TRANSACTION_C"));Pump();}finally{view.RichInput.EndChange();}await Task.Delay(250);await Idle();Require(note.Text=="OPEN_TRANSACTION_C" && new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("OPEN_TRANSACTION_C")&&!view.RichInput.CanUndo,"open native transaction rejection must safely retry latest after empty EndChange");Require(requestedPumps==2,"both real native pump boundaries must be attempted");Console.WriteLine($"PASS: nested native pump requests={requestedPumps}, framework-suspended refusals={refusedPumps}; latest source and empty-EndChange retry checked");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichExceptionPurgeRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="exception before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;
        view.RichInput.TextChanged+=(_,_)=>{if(armed){armed=false;live=false;view.ClearSensitive();throw new InvalidOperationException("SYNTHETIC_POST_ATTACH_HANDLER_FAILURE");}};
        workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("exception next"));await Idle();Require(Field<StyledDocument?>(view,"projected") is null && !Field<DispatcherTimer>(view,"transactionRetry").IsEnabled && view.RichInput.Document.Blocks.Count==0&&!view.RichInput.CanUndo,"post-attach exception after conceal must not resurrect cached source or transaction timer");workspace.Clear();window.Close();
    }
    private static Task RichClearExceptionRun()=>RichClearExceptionCase(false);
    private static async Task RichClearExceptionCase(bool urlBox)
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="clear synthetic";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();var url=Field<TextBox>(view,"linkInput");url.Text="https://example.invalid/synthetic-clear";TextChangedEventHandler throwOnClear=(_,_)=>{if(Field<bool>(view,"disposed"))throw new InvalidOperationException("SYNTHETIC_THROW_ON_CLEAR");};if(urlBox)url.TextChanged+=throwOnClear;else view.RichInput.TextChanged+=throwOnClear;bool threw=false;live=false;try{view.ClearSensitive();}catch{threw=true;}Require(!threw && Field<StyledDocument?>(view,"projected") is null && Field<NoteDraft?>(view,"note") is null && Field<EditingWorkspace?>(view,"workspace") is null && Field<Func<bool>?>(view,"current") is null && !Field<DispatcherTimer>(view,"transactionRetry").IsEnabled && url.Text.Length==0&&!url.CanUndo&&view.RichInput.Document.Blocks.Count==0&&!view.RichInput.CanUndo,"native clear exception must not interrupt reference/observer/Undo purge");workspace.Clear();window.Close();
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-clear-throw-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var draft=session.Workspace.CreateNote();draft.Text="production clear synthetic";Invoke(main,"RefreshNotes",draft);session.Workspace.ConvertMode(draft,"rich",true);await Idle();var host=Control<ContentControl>(main,"StructuredHost");var editor=(StructuredNoteEditor)host.Content;var productionUrl=Field<TextBox>(editor,"linkInput");productionUrl.Text="https://example.invalid/synthetic-production-clear";TextChangedEventHandler productionThrow=(_,_)=>{if(Field<bool>(editor,"disposed"))throw new InvalidOperationException("SYNTHETIC_THROW_ON_PRODUCTION_CLEAR");};if(urlBox)productionUrl.TextChanged+=productionThrow;else editor.RichInput.TextChanged+=productionThrow;Require(await session.SaveAsync(),"clear exception fixture baseline save");bool locked=false;try{locked=await session.LockAsync();}catch{}await Idle();Require(locked&&session.KeysReleased&&host.Content is null&&Field<StructuredNoteEditor?>(main,"structuredEditor") is null&&Field<StyledDocument?>(editor,"projected") is null&&Field<EditingWorkspace?>(editor,"workspace") is null,"native clear exception must not interrupt parent detach or key-releasing LockAsync");Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
        }
        finally
        {
            if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)try{await session.LockAsync();}catch{}SetField(main,"confirmedExit",true);main.Close();session?.Dispose();}
            CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static async Task RichCommitRaceRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;
        workspace.Changed+=()=>{if(armed){armed=false;workspace.SetRichDocument(note,RichDocumentCodec.FromPlain("REENTRANT_B"));}};
        ((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.Add(new Run("_A"));await Idle();Require(note.Text=="REENTRANT_B" && new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("REENTRANT_B") && !view.RichInput.CanUndo,"reentrant canonical B during own A commit must rebuild latest and invalidate stale A Undo");
        ((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.Add(new Run("_next"));await Idle();Require(note.Text=="REENTRANT_B_next","next native typing must preserve reentrant B not overwrite from stale A view");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichViewsRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="공유 합성 rich";workspace.ConvertMode(note,"rich",true);bool active=true;
        using var first=new StructuredNoteEditor(workspace,note,()=>active,_=>{});using var second=new StructuredNoteEditor(workspace,note,()=>active,_=>{});
        var left=new Window{Content=first,Width=500,Height=400};var right=new Window{Content=second,Width=500,Height=400};left.Show();right.Show();await Idle();
        Require(new TextRange(first.RichInput.Document.ContentStart,first.RichInput.Document.ContentEnd).Text.Contains("공유 합성 rich"),"canonical rich source must project to real WPF document");
        first.RichInput.Selection.Select(first.RichInput.Document.ContentStart,first.RichInput.Document.ContentEnd);first.ApplyBold();await Idle();
        Require(note.Document!.SourceJson.Contains("\"bold\":true") && new TextRange(second.RichInput.Document.ContentStart,second.RichInput.Document.ContentEnd).Text.Contains(note.Text),"formatting-only source changes propagate between independent WPF documents");
        first.ApplyFontSize(22);await Idle();Require(note.Document!.SourceJson.Contains("22"),"font size formatting stores source value");
        Require(first.RichInput.CanUndo,"own native rich edit must retain real Undo");first.RichInput.Undo();await Idle();Require(!note.Document!.SourceJson.Contains("\"fontSize\":22") && first.RichInput.CanRedo,"native Undo must update shared canonical without losing Redo");first.RichInput.Redo();await Idle();Require(note.Document!.SourceJson.Contains("\"fontSize\":22"),"native Redo must update canonical source");
        second.RichInput.Selection.Select(second.RichInput.Document.ContentStart,second.RichInput.Document.ContentEnd);second.ApplyBold();await Idle();Require(!first.RichInput.CanUndo&&!first.RichInput.CanRedo,"external canonical change must invalidate stale first-view Undo/Redo");
        workspace.AcceptPrepared(workspace.Capture());string before=JsonSerializer.Serialize(workspace.Capture());second.ApplyPreferences(new(true,18,1.2));await Idle();Require(JsonSerializer.Serialize(workspace.Capture())==before,"theme/font/view preferences must not rewrite run source or create content revisions");
        var unsafePaste=new DataObject();unsafePaste.SetData(DataFormats.Xaml,"<Paragraph xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Run>UNSAFE</Run></Paragraph>");unsafePaste.SetData(DataFormats.Rtf,"{\\rtf1 UNSAFE}");first.PasteData(unsafePaste);Require(JsonSerializer.Serialize(workspace.Capture())==before,"default rich clipboard formats must be refused without canonical/view mutation");
        var safePaste=new DataObject();safePaste.SetData(DataFormats.UnicodeText,"한글👩‍💻e\u0301");first.PasteData(safePaste);await Idle();Require(note.Text.Contains("한글👩‍💻e\u0301"),"bounded UnicodeText paste updates canonical source");
        active=false;first.ClearSensitive();second.ClearSensitive();workspace.Clear();await Idle();
        Require(first.RichInput.Document.Blocks.Count==0 && second.RichInput.Document.Blocks.Count==0 && !first.RichInput.CanUndo && !second.RichInput.CanUndo && note.Document is null,"conceal/clear cannot save empty projection and must remove document/source/Undo");
        left.Close();right.Close();
    }
    private static async Task RichFidelityRun()
    {
        const string source="""
        {"nodes":[{"type":"paragraph","runs":[{"text":"CR\r\nLF\t한글👩‍💻é","bold":true,"underline":true,"strike":true,"fontSize":22,"fontFamily":"Segoe UI","foreground":"#123456","background":"#ffee00","link":"https://example.invalid/inert?q=synthetic"}]},{"type":"checklist","items":[{"checked":false,"runs":[{"text":"[x] literal body"}]}]},{"type":"list","ordered":true,"items":[{"runs":[{"text":"one"}]},{"runs":[{"text":"two"}]}]},{"type":"table","rows":[[{"runs":[{"text":"cell1"}]},{"runs":[{"text":"cell2","bold":true}]}]]},{"type":"paragraph","runs":[{"text":""}]}]}
        """;
        var document=new StyledDocument(1,source);var info=RichDocumentCodec.Inspect(document);var now=DateTimeOffset.UtcNow;var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"fidelity",info.Text!,"rich"){Document=document}]);var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var note=workspace.Notes.Single();bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=500,Height=400};window.Show();await Idle();
        Require(!view.RichInput.IsReadOnly && RichDocumentCodec.Inspect((StyledDocument)Invoke(view,"CaptureDocument")!).Text==info.Text,"all supported nodes/styles/link/CRLF/tab/Unicode/trailing paragraph must roundtrip before editing; synthetic native="+SyntheticRenderDiagnostic(document));
        view.ApplyPreferences(new(true,24,1.5));((Paragraph)view.RichInput.Document.Blocks.LastBlock).Inlines.Add(new Run("after preferences"));await Idle();
        using(var saved=JsonDocument.Parse(note.Document!.SourceJson))
        {
            var first=saved.RootElement.GetProperty("nodes")[0].GetProperty("runs")[0];Require(first.GetProperty("foreground").GetString()!.EndsWith("123456") && first.GetProperty("background").GetString()!.EndsWith("FFEE00") && first.GetProperty("fontSize").GetDouble()==22 && first.GetProperty("fontFamily").GetString()=="Segoe UI" && first.GetProperty("link").GetString()=="https://example.invalid/inert?q=synthetic","preference then first native typing preserves canonical explicit style/link");
            var fresh=saved.RootElement.GetProperty("nodes")[4].GetProperty("runs").EnumerateArray().Last();Require(!fresh.TryGetProperty("foreground",out _)&&!fresh.TryGetProperty("fontSize",out _)&&!fresh.TryGetProperty("fontFamily",out _),"display inherited font/theme must not become new canonical marks");
            Require(saved.RootElement.GetProperty("nodes")[1].GetProperty("items")[0].GetProperty("runs")[0].GetProperty("text").GetString()=="[x] literal body","checklist scaffold must not consume literal-looking body prefix");
        }
        workspace.AcceptPrepared(workspace.Capture());string before=note.Document!.SourceJson;var checklist=(List)view.RichInput.Document.Blocks.ElementAt(1);((Run)((Paragraph)checklist.ListItems.FirstListItem.Blocks.FirstBlock).Inlines.FirstInline).Text="";await Idle();Require(note.Document!.SourceJson==before && !view.RichInput.CanUndo,"partial checklist scaffold deletion rolls back canonical and purges Undo");
        view.RichInput.Document.Blocks.Add(new Section(new Paragraph(new Run("unsupported native"))));await Idle();Require(note.Document!.SourceJson==before && !view.RichInput.CanUndo,"unsupported native block refuses without silent flattening");
        live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichFormattingCommandsRun()
    {
        var errors=new List<string>();
        var cases=new (string Method,object[] Arguments,string Expected)[]{("ApplyFontFamily",["Arial"],"Arial"),("ApplyUnderline",[],"\"underline\":true"),("ApplyStrike",[],"\"strike\":true"),("ApplyForeground",["#123456"],"123456"),("ApplyHighlight",["#FFEE00"],"FFEE00"),("ToggleList",[false],"\"ordered\":false"),("ToggleList",[true],"\"ordered\":true"),("InsertChecklist",[],"\"type\":\"checklist\""),("InsertTable",[2,2],"\"type\":\"table\""),("ApplyLink",["https://example.invalid/user-link"],"https://example.invalid/user-link")};
        foreach(var item in cases)
        {
            var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="alpha";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=500,Height=400};window.Show();await Idle();
            try
            {
                var paragraph=(Paragraph)view.RichInput.Document.Blocks.FirstBlock;view.RichInput.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
                var command=typeof(StructuredNoteEditor).GetMethod(item.Method,BindingFlags.Instance|BindingFlags.Public)??throw new NotImplementedException("Missing canonical command "+item.Method);command.Invoke(view,item.Arguments);await Idle();Require(note.Document!.SourceJson.Contains(item.Expected),"canonical formatting command stores source: "+item.Method);
                if(item.Method=="InsertChecklist")
                {
                    var toggle=typeof(StructuredNoteEditor).GetMethod("ToggleChecked",BindingFlags.Instance|BindingFlags.Public)??throw new NotImplementedException("Missing canonical command ToggleChecked");toggle.Invoke(view,[]);await Idle();Require(note.Document!.SourceJson.Contains("\"checked\":true"),"checklist toggle stores bool/source not only glyph");
                }
                if(item.Method=="InsertTable")
                {
                    var table=view.RichInput.Document.Blocks.OfType<Table>().Single();((Run)((Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock).Inlines.FirstInline).Text="표 합성 👩‍💻";await Idle();Require(note.Text.Contains("표 합성 👩‍💻") && note.Document!.SourceJson.Contains("table"),"native table cell edit updates complete canonical table");
                }
                Console.WriteLine("PASS: rich command "+item.Method);
            }
            catch(Exception error){errors.Add(item.Method+": "+error.GetBaseException().Message);}
            finally{live=false;view.ClearSensitive();workspace.Clear();window.Close();}
        }
        Require(errors.Count==0,string.Join(" / ",errors));
    }
    private static async Task RichLinkSplitRun()
    {
        var json=System.Text.Json.Nodes.JsonNode.Parse(RichDocumentCodec.FromPlain("alpha beta gamma").SourceJson)!;json["nodes"]![0]!["runs"]![0]!["link"]="https://example.invalid/split-link";var document=new StyledDocument(1,json.ToJsonString());var now=DateTimeOffset.UtcNow;var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"link split","alpha beta gamma","rich"){Document=document}]);var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var note=workspace.Notes.Single();bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=500,Height=400};window.Show();await Idle();Require(!view.RichInput.IsReadOnly,"supported linked source must allow editing");
        var run=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;view.RichInput.Selection.Select(run.ContentStart.GetPositionAtOffset(6)!,run.ContentStart.GetPositionAtOffset(10)!);view.ApplyBold();await Idle();
        using(var saved=JsonDocument.Parse(note.Document!.SourceJson)){var runs=saved.RootElement.GetProperty("nodes")[0].GetProperty("runs").EnumerateArray().Where(r=>r.GetProperty("text").GetString()!.Length!=0).ToArray();Require(runs.Length>=2 && runs.All(r=>r.TryGetProperty("link",out var link)&&link.GetString()=="https://example.invalid/split-link") && note.Text=="alpha beta gamma","native run splitting must retain every link-marked fragment and exact text");}
        live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichHistoryRun()
    {
        var plain=RichDocumentCodec.FromPlain("same projected body");var json=System.Text.Json.Nodes.JsonNode.Parse(plain.SourceJson)!;json["nodes"]![0]!["runs"]![0]!["bold"]=true;var bold=new StyledDocument(1,json.ToJsonString());var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var old=Guid.NewGuid();var snapshot=new VaultSnapshot(4,Guid.NewGuid(),[new(id,Guid.NewGuid(),[old],now,now,"same title","same projected body","rich"){Document=plain}]){History=[new(id,old,[],now,"same title","same projected body"){Mode="rich",Document=bold}]};var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var note=workspace.Notes.Single();var history=new HistoryWindow(note,snapshot.History,_=>{},snapshot.Notes.Single());history.Show();await Idle();Require(Control<TextBlock>(history,"ComparisonInfo").Text.Contains("서식 변경"),"history must distinguish rich format changes when projected text is identical");Require(Control<TextBlock>(history,"DiffState").Text.Contains("텍스트"),"comparison must explicitly label text-only diff scope separately from rich source changes");history.Close();Require(Field<object?>(history,"comparisonSources") is null,"closed rich history must release full source comparison records");workspace.Clear();
    }
    private static async Task RichNativeStyleRun()
    {
        foreach(int kind in new[]{0,1,2})
        {
            var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="style refusal";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();string before=note.Document!.SourceJson;var run=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;
            if(kind==0)run.FontStretch=FontStretches.Condensed;else if(kind==1)run.BaselineAlignment=BaselineAlignment.Subscript;else run.TextDecorations=TextDecorations.OverLine;await Idle();
            var restored=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;Require(note.Document!.SourceJson==before && restored.FontStretch==FontStretches.Normal && restored.BaselineAlignment==BaselineAlignment.Baseline && restored.TextDecorations?.Any(d=>d.Location==TextDecorationLocation.OverLine)!=true && !view.RichInput.CanUndo,"unsupported native stretch/baseline/overline must rollback view/source and purge Undo");live=false;view.ClearSensitive();workspace.Clear();window.Close();
        }
    }
    private static async Task RichMalformedNativeRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="malformed before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();string before=note.Document!.SourceJson;
        ((Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline).Text="MALFORMED_\uD800";await Idle();Require(note.Document!.SourceJson==before && note.Text=="malformed before" && !view.RichInput.CanUndo && new TextRange(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd).Text.Contains("malformed before"),"malformed native Unicode must reject before JSON can replace it with U+FFFD");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichCompositionRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="composition before";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();
        void Raise(RoutedEvent routed)=>view.RichInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,view.RichInput,"",TextCompositionAutoComplete.Off)){RoutedEvent=routed});
        Raise(TextCompositionManager.PreviewTextInputStartEvent);var run=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;run.Text="TRANSIENT_\uD800";await Idle();Require(note.Text=="composition before" && ReferenceEquals(((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline,run),"composition transient Unicode must remain local without canonical mutation or destructive rollback");
        run.Text="완성👩‍💻é";Raise(TextCompositionManager.PreviewTextInputEvent);await Idle();Require(note.Text=="완성👩‍💻é","completed composition event must validate and publish complete Unicode source");
        string beforeNext=note.Document!.SourceJson;Raise(TextCompositionManager.PreviewTextInputStartEvent);run=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;run.Text="COMPLETE_A";Raise(TextCompositionManager.PreviewTextInputEvent);Raise(TextCompositionManager.PreviewTextInputStartEvent);run.Text="TRANSIENT_B_\uD800";await Idle();Require(note.Document!.SourceJson==beforeNext && ReferenceEquals(((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline,run),"stale completion A must not finish or rollback newly started composition B");run.Text="완성 B";Raise(TextCompositionManager.PreviewTextInputEvent);await Idle();Require(note.Text=="완성 B","only matching composition B completion may publish final source");
        Raise(TextCompositionManager.PreviewTextInputStartEvent);run=(Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline;run.Text="LATE_COMPOSITION";Raise(TextCompositionManager.PreviewTextInputEvent);live=false;view.ClearSensitive();workspace.Clear();await Idle();Require(view.RichInput.Document.Blocks.Count==0 && !view.RichInput.CanUndo && note.Document is null,"queued composition completion cannot revive concealed content");window.Close();
    }
    private static async Task RichCompositionMetadataRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="metadata composition baseline";workspace.ConvertMode(note,"rich",true);bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();
        void Raise(RoutedEvent routed)=>view.RichInput.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,new TextComposition(InputManager.Current,view.RichInput,"",TextCompositionAutoComplete.Off)){RoutedEvent=routed});
        Raise(TextCompositionManager.PreviewTextInputStartEvent);((Run)((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.FirstInline).Text="완성 본문과 metadata";Raise(TextCompositionManager.PreviewTextInputEvent);note.Title="최신 제목";note.Favorite=true;await Idle();Require(note.Text=="완성 본문과 metadata"&&note.Title=="최신 제목"&&note.Favorite,"completed composition must preserve latest title/metadata instead of discarding completed content");
        live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichSameContentRestoreRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="same restore body";workspace.ConvertMode(note,"rich",true);workspace.AcceptPrepared(workspace.Capture());bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();view.RichInput.Selection.Select(view.RichInput.Document.ContentStart,view.RichInput.Document.ContentEnd);view.ApplyBold();await Idle();workspace.AcceptPrepared(workspace.Capture());note.Title="latest title";workspace.SetTags(note,["synthetic tag"]);await Idle();Require(view.RichInput.CanUndo,"metadata-only staging must preserve valid local native Undo");var revision=workspace.HistoryFor(note).First(r=>r.Mode=="rich"&&r.Document==note.Document);string source=note.Document!.SourceJson;workspace.RestoreRevision(note,revision.RevisionId);await Idle();Require(note.Document!.SourceJson==source&&!view.RichInput.CanUndo&&!view.RichInput.CanRedo,"explicit same-content restore must invalidate stale native Undo/Redo even when document equality is unchanged");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static async Task RichOwnSameRestoreRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="same callback restore";workspace.ConvertMode(note,"rich",true);workspace.AcceptPrepared(workspace.Capture());bool live=true;using var view=new StructuredNoteEditor(workspace,note,()=>live,_=>{});var window=new Window{Content=view,Width=400,Height=300};window.Show();await Idle();bool armed=true;workspace.Changed+=()=>{if(!armed)return;armed=false;workspace.AcceptPrepared(workspace.Capture());workspace.SetTags(note,["callback tag"]);var revision=workspace.HistoryFor(note).First(r=>r.Document==note.Document&&r.Mode=="rich");workspace.RestoreRevision(note,revision.RevisionId);};((Paragraph)view.RichInput.Document.Blocks.FirstBlock).Inlines.Add(new Run("_typed"));await Idle();Require(note.Text=="same callback restore_typed"&&!view.RichInput.CanUndo&&!view.RichInput.CanRedo,"same-content restore inside own commit callback must invalidate authority instead of acknowledging stale native Undo");live=false;view.ClearSensitive();workspace.Clear();window.Close();
    }
    private static string MarkdownDiagnostic(MarkdownNotePreview view)
    {
        var panel=Field<StackPanel>(view,"content");return $"blocks={panel.Children.Count}, state={Field<TextBlock>(view,"state").Text}, generation={Field<long>(view,"requestedGeneration")}, posted={Field<bool>(view,"posted")}, disposed={Field<bool>(view,"disposed")}, DP-text={string.Join("|",panel.Children.OfType<TextBlock>().Select(b=>b.Text))}, range-text={string.Join("|",panel.Children.OfType<TextBlock>().Select(b=>new TextRange(b.ContentStart,b.ContentEnd).Text))}";
    }
    private static string MarkdownText(MarkdownNotePreview view)=>string.Join("\n",Field<StackPanel>(view,"content").Children.OfType<TextBlock>().Select(block=>new TextRange(block.ContentStart,block.ContentEnd).Text));
    private static async Task MarkdownPreviewRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();const string raw="# 합성 Markdown\r\n\r\n**굵게** `코드` 👩‍💻é\n\n<script>그대로</script>\n\n![그림](https://example.invalid/inert) [링크](file:///inert)";note.Text=raw;workspace.ConvertMode(note,"markdown",true);workspace.AcceptPrepared(workspace.Capture());bool live=true;using var first=new MarkdownNotePreview(note,()=>live);using var second=new MarkdownNotePreview(note,()=>live);var left=new Window{Content=first,Width=500,Height=400};var right=new Window{Content=second,Width=500,Height=400};left.Show();right.Show();string original=JsonSerializer.Serialize(workspace.Capture());await first.WhenIdle;await second.WhenIdle;await Idle();Require(MarkdownText(first).Contains("합성 Markdown")&&MarkdownText(first).Contains("<script>그대로</script>")&&MarkdownText(second).Contains("👩‍💻é"),"readonly Markdown renders safe inert Unicode/HTML labels in two independent WPF views; "+MarkdownDiagnostic(first)+" / "+MarkdownDiagnostic(second));Require(note.Text==raw&&JsonSerializer.Serialize(workspace.Capture())==original,"readonly Markdown view cannot normalize raw CRLF/source or create revisions");
        note.Text="# 최신 합성 preview\n\n7) first\n8) second\n   - nested\n\n3. # numbered heading\n4. ```\n   numbered code\n   ```";workspace.AcceptPrepared(workspace.Capture());original=JsonSerializer.Serialize(workspace.Capture());await Task.Delay(300);await first.WhenIdle;await second.WhenIdle;await Idle();Require(MarkdownText(first).Contains("최신 합성")&&MarkdownText(second).Contains("최신 합성")&&JsonSerializer.Serialize(workspace.Capture())==original,"debounced latest raw source propagates without source writeback");Require(MarkdownText(first).Contains("7) first")&&MarkdownText(first).Contains("8) second")&&MarkdownText(second).Contains("• nested")&&MarkdownText(first).Contains("3. numbered heading")&&MarkdownText(second).Contains("4. numbered code"),"Actual WPF Markdown preserves ordered starts/delimiter and nested unordered markers");live=false;first.Dispose();second.Dispose();workspace.Clear();await Idle();Require(Field<StackPanel>(first,"content").Children.Count==0&&Field<StackPanel>(second,"content").Children.Count==0&&Field<NoteDraft?>(first,"note") is null,"Markdown conceal clears native output and note ownership");left.Close();right.Close();
        workspace=new EditingWorkspace(TimeProvider.System);note=workspace.CreateNote();note.Text="LATE_SYNTHETIC_MARKDOWN";workspace.ConvertMode(note,"markdown",true);live=true;using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var delayed=new MarkdownNotePreview(note,()=>live,source=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new Exception("synthetic WPF preview gate timeout");return SafeMarkdown.Preview(source);});var delayedWindow=new Window{Content=delayed,Width=400,Height=300};delayedWindow.Show();Require(entered.Wait(TimeSpan.FromSeconds(5)),"delayed real parser enters");Task active=delayed.WhenIdle;live=false;delayed.Dispose();workspace.Clear();release.Set();await active;await Idle();Require(Field<StackPanel>(delayed,"content").Children.Count==0&&Field<NoteDraft?>(delayed,"note") is null,"executing parser late completion cannot revive revoked Markdown output");delayedWindow.Close();
    }
    private static async Task MarkdownSlowDeliveryRun()
    {
        var workspace=new EditingWorkspace(TimeProvider.System);var note=workspace.CreateNote();note.Text="# SLOW_INITIAL";workspace.ConvertMode(note,"markdown",true);bool live=true;using var view=new MarkdownNotePreview(note,()=>live);var window=new Window{Content=view,Width=400,Height=300};window.Show();view.WhenIdle.GetAwaiter().GetResult();
        // Intentionally do not pump UI while real parser completions arrive. Only the latest result is retained.
        for(int index=0;index<30;index++){note.Text="# SLOW_LATEST_"+index;Invoke(view,"RequestCurrent");view.WhenIdle.GetAwaiter().GetResult();Require(Field<bool>(view,"posted")&&Field<MarkdownPreviewUpdate?>(view,"pendingResult") is not null,"slow UI has one posted wake and one latest completed result");}
        await Idle();Require(MarkdownText(view).Contains("SLOW_LATEST_29")&&!MarkdownText(view).Contains("SLOW_LATEST_28"),"coalesced delivery publishes only latest authorized source after blocked UI resumes; "+MarkdownDiagnostic(view));
        note.Text="# QUEUED_BEFORE_REVOKE";Invoke(view,"RequestCurrent");view.WhenIdle.GetAwaiter().GetResult();live=false;view.Dispose();workspace.Clear();await Idle();Require(Field<StackPanel>(view,"content").Children.Count==0&&Field<MarkdownPreviewUpdate?>(view,"pendingResult") is null&&Field<string?>(view,"requestedSource") is null&&!Field<DispatcherTimer>(view,"debounce").IsEnabled,"already-posted delivery cannot revive output/source/debounce after revoke");window.Close();
    }
    private static async Task EditorSameTurnModeRun(string selectedMode)
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-mode-turn-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();note.Text="# SAME_TURN_SOURCE";Invoke(main,"RefreshNotes",note);
            foreach(string mode in new[]{selectedMode})
            {
                session.Workspace.ConvertMode(note,mode,true);Invoke(main,"RefreshNotes",note);await Idle();session.Workspace.ConvertMode(note,"plain",true);session.Workspace.ConvertMode(note,mode,true);Invoke(main,"RefreshNotes",note);await Idle();
                if(mode=="rich"){var editor=Control<ContentControl>(main,"StructuredHost").Content as StructuredNoteEditor;Require(editor is not null&&!Field<bool>(editor,"disposed")&&new TextRange(editor.RichInput.Document.ContentStart,editor.RichInput.Document.ContentEnd).Text.Contains("SAME_TURN_SOURCE"),"same-turn rich/plain/rich must recreate a self-disposed rich view");}
                else{var preview=Control<ContentControl>(main,"MarkdownHost").Content as MarkdownNotePreview;Require(preview is not null&&!Field<bool>(preview,"disposed"),"same-turn Markdown/plain/Markdown must recreate self-disposed preview");await preview!.WhenIdle;await Idle();Require(MarkdownText(preview).Contains("SAME_TURN_SOURCE"),"recreated Markdown view must publish current source");}
            }
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
        }
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)try{await session.LockAsync();}catch{}SetField(main,"confirmedExit",true);main.Close();session?.Dispose();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task MarkdownProductionRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-markdown-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();note.Text="# 관리창 원문\r\n\r\n**합성**";Invoke(main,"RefreshNotes",note);session.Workspace.ConvertMode(note,"markdown",true);await Idle();var host=main.FindName("MarkdownHost") as ContentControl;Require(host?.Content is MarkdownNotePreview&&BindingOperations.IsDataBound(Control<TextBox>(main,"BodyEditor"),TextBox.TextProperty),"management Markdown keeps raw binding and adds readonly preview");Invoke(main,"OpenSticky",note);await Idle();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var stickyHost=sticky.FindName("MarkdownHost") as ContentControl;Require(stickyHost?.Content is MarkdownNotePreview&&BindingOperations.IsDataBound(Control<TextBox>(sticky,"BodyEditor"),TextBox.TextProperty),"sticky Markdown shares exact raw text and independent readonly preview");Require(await session.SaveAsync(),"Markdown production source save");await session.LockAsync();await Idle();Require(host!.Content is null&&stickyHost!.Content is null&&session.KeysReleased,"production conceal detaches Markdown previews before key release");Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;using var reopened=EncryptedVault.Open(root,secret);Require(reopened.Loaded.Notes.Single().Mode=="markdown"&&reopened.Loaded.Notes.Single().Text=="# 관리창 원문\r\n\r\n**합성**","production Markdown encrypted restart preserves exact raw source");
        }
        finally{if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)try{await session.LockAsync();}catch{}SetField(main,"confirmedExit",true);main.Close();session?.Dispose();}CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task RichWindowsIntegrationRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-rich-main-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");var note=session.Workspace.CreateNote();note.Text="관리창 합성 rich";Invoke(main,"RefreshNotes",note);session.Workspace.ConvertMode(note,"rich",true);await Idle();
            var host=Control<ContentControl>(main,"StructuredHost");Require(host.Content is StructuredNoteEditor,"management editor must wire canonical rich control without plain Text binding");
            Require(!BindingOperations.IsDataBound(Control<TextBox>(main,"BodyEditor"),TextBox.TextProperty),"rich management body must detach lossy plain-text binding");
            Invoke(main,"OpenSticky",note);await Idle();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var stickyHost=Control<ContentControl>(sticky,"StructuredHost");Require(stickyHost.Content is StructuredNoteEditor && !BindingOperations.IsDataBound(Control<TextBox>(sticky,"BodyEditor"),TextBox.TextProperty),"rich sticky must own separate canonical view and no plain binding");
            var editor=(StructuredNoteEditor)host.Content;editor.RichInput.Selection.Select(editor.RichInput.Document.ContentStart,editor.RichInput.Document.ContentEnd);editor.ApplyBold();await Idle();Require(note.Document!.SourceJson.Contains("\"bold\":true") && new TextRange(((StructuredNoteEditor)stickyHost.Content).RichInput.Document.ContentStart,((StructuredNoteEditor)stickyHost.Content).RichInput.Document.ContentEnd).Text.Contains(note.Text),"production management formatting propagates to sticky");
            Require(await session.SaveAsync(),"rich production WPF save");await session.LockAsync();await Idle();Require(host.Content is null && stickyHost.Content is null && editor.RichInput.Document.Blocks.Count==0 && !editor.RichInput.CanUndo,"production conceal disposes rich hosts/documents/Undo");
            Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
            using(var reopened=EncryptedVault.Open(root,secret))Require(reopened.Loaded.Notes.Single().Mode=="rich" && reopened.Loaded.Notes.Single().Document!.SourceJson.Contains("\"bold\":true"),"production rich actual encrypted restart");
        }
        finally
        {
            if(main is not null){var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsLocked)await session.LockAsync();SetField(main,"confirmedExit",true);main.Close();session?.Dispose();}
            CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static async Task BatchFailureRun()
    {
        foreach(bool byteBudget in new[]{false,true})
        {
            var root=Path.Combine(Path.GetTempPath(),"memo-wpf-batch-refusal-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
            try
            {
                var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var snapshot=new VaultSnapshot(3,Guid.NewGuid(),[new(id,Guid.NewGuid(),[],now,now,"synthetic full A",byteBudget?new string('x',3000):"a"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"synthetic B","b")]);
                var history=Enumerable.Range(0,byteBudget?255:512).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"h",byteBudget?new string('x',65536):"history")).ToArray();snapshot=snapshot with{History=history};
                if(byteBudget)
                {
                    int length=((byte[])typeof(VaultSnapshot).Assembly.GetType("MemoApp.Core.Storage.SnapshotSerialization")!.GetMethod("Bytes",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[snapshot])!).Length;
                    int last=65536-(length-(16*1024*1024-232-2400));Require(last is >=0 and <=65536,"WPF whole byte-boundary fixture");history[^1]=history[^1] with{Text=new string('x',last)};
                }
                using(var vault=EncryptedVault.Create(root,secret,secret))vault.Save(snapshot);
                main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));var session=Field<SaveCoordinator>(main,"session");var first=session.Workspace.Notes[0];Invoke(main,"OpenSticky",first);await Idle();Field<DispatcherTimer>(main,"timer").Stop();await Field<Task>(main,"recentTask");Require(await session.SaveAsync(),"WPF batch refusal baseline including settled recent UI save");await Idle();
                var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[first.Id];var list=Control<ListBox>(main,"NotesList");list.SelectAll();string before=JsonSerializer.Serialize(session.Workspace.Capture());var request=Invoke(main,"CaptureBatch",false)!;
                await (Task)Invoke(main,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
                Require(session.Workspace.Notes.All(n=>!n.IsDeleted) && JsonSerializer.Serialize(session.Workspace.Capture())==before && list.SelectedItems.Count==2 && sticky.IsVisible && sticky.Placement!.State.Open,"failed batch history/byte preflight preserves notes/UI records/selection/open sticky");
                Require(Control<TextBlock>(main,"Notice").Text.Contains("적용하지"),"preflight refusal must report unapplied batch");await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
            }
            finally
            {
                if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null){await active.LockAsync();active.Dispose();SetField(main,"session",null!);}SetField(main,"confirmedExit",true);main.Close();}
                CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
        var failingRoot=Path.Combine(Path.GetTempPath(),"memo-wpf-batch-flush-"+Guid.NewGuid().ToString("N"));var failingSecret=EncryptedVault.GenerateRecoverySecret();MainWindow? failedMain=null;
        try
        {
            var now=DateTimeOffset.UtcNow;using(var vault=EncryptedVault.Create(failingRoot,failingSecret,failingSecret))vault.Save(new(3,Guid.NewGuid(),[new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"fault A","a"),new(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"fault B","b")]));
            var original=File.ReadAllBytes(Path.Combine(failingRoot,"current.vault"));failedMain=new MainWindow(failingRoot);failedMain.Show();Invoke(failedMain,"StartSession",EncryptedVault.Open(failingRoot,failingSecret,files:new BatchFaultFiles()));var session=Field<SaveCoordinator>(failedMain,"session");Control<ListBox>(failedMain,"NotesList").SelectAll();var request=Invoke(failedMain,"CaptureBatch",false)!;
            await (Task)Invoke(failedMain,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
            Require(session.Workspace.Notes.All(n=>n.IsDeleted) && session.IsDirty && Control<TextBlock>(failedMain,"Notice").Text.Contains("변경은 반영") && Control<TextBlock>(failedMain,"Notice").Text.Contains("암호 저장 실패") && File.ReadAllBytes(Path.Combine(failingRoot,"current.vault")).SequenceEqual(original),"post-apply disk failure must preserve original and report applied unsaved batch truthfully");
            await session.LockAsync();session.Dispose();SetField(failedMain,"session",(object)null!);SetField(failedMain,"confirmedExit",true);failedMain.Close();failedMain=null;
        }
        finally
        {
            if(failedMain is not null){var active=Field<SaveCoordinator?>(failedMain,"session");if(active is not null){await active.LockAsync();active.Dispose();SetField(failedMain,"session",null!);}SetField(failedMain,"confirmedExit",true);failedMain.Close();}
            CryptographicOperations.ZeroMemory(failingSecret);if(Directory.Exists(failingRoot))Directory.Delete(failingRoot,true);
        }
    }
    private sealed class BatchFaultFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles real=new();public Stream CreateNew(string path)=>real.CreateNew(path);
        public void FlushToDisk(Stream stream)=>throw new IOException("Synthetic WPF batch flush fault");
        public void Replace(string temporary,string current,string previous)=>real.Replace(temporary,current,previous);public void Move(string temporary,string current)=>real.Move(temporary,current);
    }
    private static async Task DeviceWindowsRun()
    {
        var root=Path.Combine(Path.GetTempPath(),"memo-wpf-device-"+Guid.NewGuid().ToString("N"));var secret=EncryptedVault.GenerateRecoverySecret();var profile=Guid.NewGuid();MainWindow? main=null;
        try
        {
            main=new MainWindow(root,profile);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var session=Field<SaveCoordinator>(main,"session");
            var note=session.Workspace.CreateNote();note.Title="합성 장치 창";note.Text="DEVICE_WPF_SYNTHETIC";Require(await session.SaveAsync(),"device WPF initial save");Invoke(main,"RefreshNotes",note);
            Invoke(main,"OpenSticky",note);Invoke(main,"Calendar_Click",main,new RoutedEventArgs());Invoke(main,"Clock_Click",main,new RoutedEventArgs());
            var oldSticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var stale=oldSticky.Placement!;
            Require(session.Workspace.GetUiDevice(profile).Windows.Length==3 && session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"same-turn new memo/widget opening intention must precede native Loaded await");
            await session.LockAsync();await Idle();await stale.RestoreAsync();
            Require(!oldSticky.IsVisible && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==0,"immediate lock must close native memo/date widgets");
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"real encrypted reopen proves security conceal preserved opening intention before Loaded");
            note=session.Workspace.Notes.Single();var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var widgets=Field<Dictionary<string,DateWidgetWindow>>(main,"widgets");
            Require(widgets.Count==2 && sticky.IsVisible,"current-profile unlock must reopen saved memo/calendar/clock");await sticky.Placement!.RestoreAsync();await Idle();
            Require(sticky.Placement.State.Monitor!="DEFAULT" && sticky.Placement.State.Dpi>0,"native monitor/DPI layout must be captured");WithinWorkArea(sticky);
            var content=session.Workspace.Capture().Notes.Single();sticky.Left+=35;sticky.Top+=20;sticky.Width=430;sticky.Height=450;await Idle();
            Require(Math.Abs(sticky.Placement.State.Width-430)<2 && Math.Abs(sticky.Placement.State.Height-450)<2,"native move/resize stores DIP dimensions");
            Control<CheckBox>(main,"DarkToggle").IsChecked=true;Invoke(main,"UiPreference_Changed",main,new RoutedEventArgs());Control<Slider>(main,"FontSlider").Value=18;Control<Slider>(main,"ScaleSlider").Value=1.2;await Idle();
            Require(sticky.FontSize==18 && main.FontSize==18 && Math.Abs(Control<ScaleTransform>(sticky,"ContentScale").ScaleX-1.2)<0.001 && Field<TextBlock>(widgets["clock"],"time").FontSize==36,"global font/view scale must affect management/memo/clock");
            var history=new HistoryWindow(note,[],_=>{});Field<HashSet<HistoryWindow>>(main,"historyWindows").Add(history);history.Show();Invoke(main,"ApplyUiPreferences");
            Require(history.FontSize==18 && Control<ScaleTransform>(history,"HistoryScale").ScaleX==1.2 && Control<TextBox>(history,"PastText").Foreground==Brushes.White,"global preferences must reach independent history view");
            double unfolded=sticky.Placement.State.Height;Control<CheckBox>(sticky,"FoldToggle").IsChecked=true;await Idle();
            Require(sticky.Placement.State.Folded && sticky.Placement.State.Height==unfolded && Control<TextBox>(sticky,"BodyEditor").Visibility==Visibility.Collapsed,"fold preserves unfolded dimensions");
            Control<CheckBox>(sticky,"FoldToggle").IsChecked=false;Control<CheckBox>(sticky,"PositionToggle").IsChecked=true;sticky.Topmost=true;sticky.Opacity=0.7;await Idle();
            Require(sticky.ResizeMode==ResizeMode.CanMinimize && sticky.Placement.State.PositionLocked && sticky.Placement.State.Topmost && Math.Abs(sticky.Placement.State.Opacity-0.7)<0.001,"position/topmost/opacity state is independent and persisted");
            Require(Field<HwndSource?>(sticky.Placement,"source") is not null && PositionCommandHandled(sticky,0xF010) && PositionCommandHandled(sticky,0xF000) && PositionCommandHandled(sticky,0xF030),"attached native hook must block SC_MOVE/SC_SIZE/SC_MAXIMIZE when position locked");
            Control<CheckBox>(sticky,"PositionToggle").IsChecked=false;Require(!PositionCommandHandled(sticky,0xF010) && !PositionCommandHandled(sticky,0xF000),"unlocked native hook must leave ordinary move/size commands available");Control<CheckBox>(sticky,"PositionToggle").IsChecked=true;
            sticky.Width=2200;sticky.Height=1800;Invoke(main,"ArrangeSticky_Click",main,new RoutedEventArgs());await Idle();WithinWorkArea(sticky);
            Require(sticky.Placement.State.Width<=2000 && sticky.Placement.State.Height<=1600,"arrange must size actual HWND to work-area/schema bounds");
            Invoke(main,"ToggleSticky_Click",main,new RoutedEventArgs());Require(!sticky.IsVisible,"hide all memo native windows");Invoke(main,"ToggleSticky_Click",main,new RoutedEventArgs());Require(sticky.IsVisible,"show all memo native windows");
            Require(session.Workspace.Capture().Notes.Single()==content && session.Workspace.Capture().History.Length==0,"window/theme callbacks must not create content revisions or timestamp noise");
            stale=sticky.Placement;sticky.Close();widgets["clock"].Close();await Idle();
            Require(session.Workspace.GetUiDevice(profile).Windows.Where(w=>w.Kind is "memo" or "clock").All(w=>!w.Open),"user close must record closed independently of conceal");
            Invoke(main,"OpenSticky",note);Invoke(main,"Clock_Click",main,new RoutedEventArgs());
            Require(session.Workspace.GetUiDevice(profile).Windows.All(w=>w.Open),"same-turn closed-to-open intention precedes Loaded");await session.LockAsync();await Idle();
            Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==1 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==2,"repeat unlock must restore once without duplicate native windows");
            var before=session.Workspace.GetUiDevice(profile);await stale.RestoreAsync();await Idle();Require(session.Workspace.GetUiDevice(profile)==before,"disposed prior-session controller cannot mutate reopened session");
            Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id].Close();Field<Dictionary<string,DateWidgetWindow>>(main,"widgets")["clock"].Close();
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Keys.SequenceEqual(new[]{"calendar"}),"explicit user-close must remain closed on subsequent unlock");
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");
            SetField(main,"confirmedExit",true);main.Close();main=null;
            main=new MainWindow(root,Guid.NewGuid());main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));session=Field<SaveCoordinator>(main,"session");await Idle();
            Require(Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==0 && Field<Dictionary<string,DateWidgetWindow>>(main,"widgets").Count==0 && main.FontSize==14,"another device profile must not inherit native windows/preferences");
            await session.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
        }
        finally
        {
            if(main is not null){SetField(main,"confirmedExit",true);main.Close();var session=Field<SaveCoordinator?>(main,"session");if(session is not null&&!session.IsBusy)session.Dispose();}
            CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static bool PositionCommandHandled(StickyNoteWindow window,int command)
    {
        object[] arguments={new WindowInteropHelper(window).Handle,0x112,(IntPtr)command,IntPtr.Zero,false};
        window.Placement!.GetType().GetMethod("Hook",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window.Placement,arguments);return (bool)arguments[4];
    }
    private static void WithinWorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;Require(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle),(IntPtr)(-4)),"actual HWND must use production PerMonitorV2 manifest");Require(GetWindowRect(handle,out var rect),"native test window rectangle");var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};Require(GetMonitorInfo(MonitorFromWindow(handle,2),ref info),"native test monitor work area");
        Require(rect.Left>=info.Work.Left && rect.Top>=info.Work.Top && rect.Right<=info.Work.Right+1 && rect.Bottom<=info.Work.Bottom+1,"native restore/arrange must fit the runner work area");
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,int flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")]private static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    private static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "memo-wpf-check-" + Guid.NewGuid().ToString("N"));
        var secret = EncryptedVault.GenerateRecoverySecret(); MainWindow? main = null;
        try
        {
            main = new MainWindow(root); main.Show(); main.UpdateLayout();
            Invoke(main, "StartSession", EncryptedVault.Create(root, secret, secret));
            var session = Field<SaveCoordinator>(main, "session");
            Control<TextBox>(main, "FolderName").Text = "합성 WPF 폴더";
            Invoke(main, "CreateFolder_Click", main, new RoutedEventArgs());
            Invoke(main, "NewNote_Click", main, new RoutedEventArgs());
            var first = session.Workspace.Notes.Single();
            await Idle(); // Complete the real New-note UI/data-binding turn before text input.
            Require(first.FolderId == session.Workspace.Folders.Single().FolderId, "new-note selected folder assignment failed");
            Require(ReferenceEquals(Control<TextBox>(main, "TitleEditor").DataContext, first) && BindingOperations.IsDataBound(Control<TextBox>(main, "TitleEditor"), TextBox.TextProperty), "title editor binding/context was not active");
            EditText(Control<TextBox>(main, "TitleEditor"), "합성 WPF 제목");
            EditText(Control<TextBox>(main, "BodyEditor"), "본문 전용 합성 WPF");
            await Idle();
            Require(first.Title == "합성 WPF 제목", "title text-container edit did not update shared draft");
            Require(first.Text == "본문 전용 합성 WPF", "body text-container edit did not update shared draft");
            Require(BindingOperations.IsDataBound(Control<TextBox>(main, "BodyEditor"), TextBox.TextProperty), "editing removed body binding");
            Require(Control<TextBlock>(main,"NoteInfo").Text.Contains("본문 12글자"), "Korean body character count failed");
            EditText(Control<TextBox>(main,"BodyEditor"), "가👩‍💻e\u0301"); await Idle();
            Require(Control<TextBlock>(main,"NoteInfo").Text.Contains("본문 3글자"), "grapheme count must not split emoji/combining character");
            EditText(Control<TextBox>(main,"BodyEditor"),"본문 전용 합성 WPF"); await Idle();
            Control<TextBox>(main, "SearchInput").Text = "본문 전용";
            Control<ComboBox>(main, "SearchFieldFilter").SelectedIndex = 1;
            Require(Control<ListBox>(main, "NotesList").Items.Count == 0, "title-only search matched body");
            Control<ComboBox>(main, "SearchFieldFilter").SelectedIndex = 2;
            Require(Control<ListBox>(main, "NotesList").Items.Count == 1, "body-only search failed");
            Control<TextBox>(main, "SearchInput").Clear();
            Control<TextBox>(main, "TagsInput").Text = "합성태그"; Invoke(main, "ApplyTags_Click", main, new RoutedEventArgs());
            Require(first.Metadata.TagIds.Length == 1, "tag control apply failed");
            Require(await session.SaveAsync(), "initial UI snapshot save failed");
            first.Text = "현재 합성 버전"; Require(await session.SaveAsync(), "UI history save failed");
            Invoke(main, "OpenSticky_Click", main, new RoutedEventArgs());
            var sticky = Field<Dictionary<Guid, StickyNoteWindow>>(main, "stickyWindows")[first.Id];
            await Idle(); EditText(Control<TextBox>(sticky, "BodyEditor"), "공유 포스트잇 수정"); await Idle();
            Require(first.Text == "공유 포스트잇 수정" && Control<TextBox>(main, "BodyEditor").Text == first.Text, "sticky/management shared binding failed");
            Invoke(main,"NewNote_Click", main, new RoutedEventArgs()); await Idle();
            var other = session.Workspace.Notes.Single(n=>n.Id!=first.Id); EditText(Control<TextBox>(main,"TitleEditor"),"합성 순서 메모"); await Idle();
            Require(Control<ListBox>(main,"NotesList").SelectionMode==SelectionMode.Extended,"batch list must allow explicit Extended selection");
            Require(await session.SaveAsync(),"batch WPF starting basis save");
            var list=Control<ListBox>(main,"NotesList");Control<ComboBox>(main,"FolderFilter").SelectedIndex=0;list.SelectedItems.Clear();list.SelectedItems.Add(other);list.SelectedItems.Add(first);await Idle();
            Require(Control<FrameworkElement>(main,"Editor").DataContext is null && !Control<FrameworkElement>(main,"Editor").IsEnabled && !Control<Button>(main,"DuplicateButton").IsEnabled,"multiple selection must revoke singleton editor/binding/actions");
            int countBefore=session.Workspace.Notes.Count;var singleBefore=session.Workspace.Capture();
            foreach(string handler in new[]{"Duplicate_Click","ApplyTags_Click","OrderUp_Click","OpenSticky_Click","History_Click"})Invoke(main,handler,main,new RoutedEventArgs());
            Control<ComboBox>(main,"MoveFolder").SelectedIndex=0;Control<ComboBox>(main,"ColorPicker").SelectedIndex=2;await Idle();
            Require(session.Workspace.Notes.Count==countBefore && session.Workspace.Capture().Notes.SequenceEqual(singleBefore.Notes) && Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows").Count==1 && Field<HashSet<HistoryWindow>>(main,"historyWindows").Count==0,"direct singleton handlers must reject multiple selection without notes/windows/history changes");
            var batchFolder=session.Workspace.CreateFolder("합성 batch 목표");Invoke(main,"RefreshFolders");await Idle();var batchTarget=Control<ComboBox>(main,"BatchFolder");batchTarget.SelectedItem=batchTarget.Items.Cast<object>().Single(item=>(Guid?)item.GetType().GetProperty("Id")!.GetValue(item)==batchFolder.FolderId);
            Invoke(main,"BatchMove_Click",main,new RoutedEventArgs());await WaitUntil(()=>!session.IsBusy);await Idle();
            Require(first.FolderId==batchFolder.FolderId && other.FolderId==batchFolder.FolderId && list.SelectedItems.Count==2,"actual batch move control preserves surviving selection and moves all selected drafts");
            var request=Invoke(main,"CaptureBatch",false)!;await (Task)Invoke(main,"ApplyBatch",request,"delete",(object)null!)!;await Idle();
            Require(first.IsDeleted && other.IsDeleted && !sticky.IsVisible,"batch delete must close sticky only after successful Deleted publication");
            Control<ComboBox>(main,"ViewFilter").SelectedIndex=4;list.SelectAll();request=Invoke(main,"CaptureBatch",true)!;await (Task)Invoke(main,"ApplyBatch",request,"restore",(object)null!)!;
            Control<ComboBox>(main,"ViewFilter").SelectedIndex=0;list.SelectedItems.Clear();list.SelectedItem=other;await Idle();
            Control<ComboBox>(main,"SortFilter").SelectedIndex=3; Invoke(main,"OrderUp_Click",main,new RoutedEventArgs()); await Idle();
            Require(ReferenceEquals(Control<ListBox>(main,"NotesList").Items[0],other), "UI custom order up action failed");
            session.Workspace.DeleteNote(other); Invoke(main,"RefreshNotes",first); await Idle();
            Invoke(main,"History_Click",main,new RoutedEventArgs());await WaitUntil(()=>Field<HashSet<HistoryWindow>>(main,"historyWindows").Count==1);var history=Field<HashSet<HistoryWindow>>(main,"historyWindows").Single();
            Require(Control<TextBox>(history, "PastText").Text.Length > 0, "history preview failed");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count>1 && Control<ComboBox>(history,"RightRevision").Items.Count>1 && Control<TextBox>(history,"DiffText").Text.Length>0,"history comparison must include current head and dated immutable revisions");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count==session.Workspace.HistoryFor(first).Count+1 && (bool)Control<ComboBox>(history,"RightRevision").SelectedItem.GetType().GetProperty("Current")!.GetValue(Control<ComboBox>(history,"RightRevision").SelectedItem)! ,"comparison includes exact current head and every distinct history revision even when date labels repeat");
            string frozenRight=Control<TextBox>(history,"RightText").Text;first.Text="AFTER_HISTORY_WINDOW_SYNTHETIC";await Idle();Require(Control<TextBox>(history,"RightText").Text==frozenRight,"later draft edit cannot replace captured comparison head");first.Text="공유 포스트잇 수정";
            Control<TextBox>(main, "TagFilter").Text = "합성태그";
            var staleBatch=Invoke(main,"CaptureBatch",false)!;
            await session.LockAsync(); await Idle();
            Require(first.IsClosed && first.Text == "" && session.KeysReleased, "Core lock revocation failed");
            Require(Control<FrameworkElement>(main, "EditingPanel").Visibility == Visibility.Collapsed && Control<ListBox>(main, "NotesList").Items.Count == 0, "lock left results visible/bound");
            Require(Control<TextBox>(main, "SearchInput").Text == "" && Control<TextBox>(main, "TagFilter").Text == "" && Control<TextBox>(main, "TagsInput").Text == "", "lock left query/tag data");
            Require(new[] { "SearchInput", "TagFilter", "FolderName", "TagsInput" }.All(name => !Control<TextBox>(main, name).CanUndo), "lock left search/organization undo text");
            Require(Control<TextBox>(main, "BodyEditor").Text == "" && !Control<TextBox>(main, "BodyEditor").CanUndo && !Control<TextBox>(main, "TitleEditor").CanUndo, "lock left text/undo plaintext");
            Require(Control<ComboBox>(main, "FolderFilter").Items.Count == 0 && Control<TextBlock>(main, "Counts").Text == "", "lock left organization/counts");
            Require(!sticky.IsVisible && sticky.DataContext is null && Control<TextBox>(sticky, "BodyEditor").Text == "" && !Control<TextBox>(sticky, "BodyEditor").CanUndo, "lock left sticky plaintext/undo");
            Require(!history.IsVisible && Control<TextBox>(history, "PastText").Text == "" && Control<ListBox>(history, "Revisions").Items.Count == 0, "lock left history plaintext");
            Require(Control<ComboBox>(history,"LeftRevision").Items.Count==0 && Control<ComboBox>(history,"RightRevision").Items.Count==0 && new[]{"LeftText","RightText","LeftTitle","RightTitle","DiffText"}.All(name=>Control<TextBox>(history,name).Text==""&&!Control<TextBox>(history,name).CanUndo) && Control<TextBlock>(history,"ComparisonInfo").Text=="","lock must clear both comparison sources/previews/diff/metadata/Undo");
            Require(Field<object?>(history,"comparisonSources") is null,"lock must release cached comparison source references");
            Invoke(main, "ReleaseSettledSession");
            Invoke(main, "StartSession", EncryptedVault.Open(root, secret));
            var reopened = Field<SaveCoordinator>(main, "session");
            await (Task)Invoke(main,"ApplyBatch",staleBatch,"delete",(object)null!)!;
            Require(reopened.Workspace.Notes.Single(n=>!n.IsDeleted).Text == "공유 포스트잇 수정", "WPF lock/reopen persisted latest");
            var reopenedNote = reopened.Workspace.Notes.Single(n=>!n.IsDeleted); reopened.Workspace.DeleteNote(reopenedNote);
            Control<ComboBox>(main, "ViewFilter").SelectedIndex = 4; await Idle();
            Require(Control<ListBox>(main, "NotesList").Items.Count == 2 && !Control<FrameworkElement>(main, "Editor").IsEnabled && Control<Button>(main, "RestoreButton").IsEnabled, "trash selection must show readonly content and restore action");
            reopened.Workspace.RestoreNote(reopenedNote); Control<ComboBox>(main, "ViewFilter").SelectedIndex = 0; await Idle();
            Require(Control<FrameworkElement>(main, "Editor").IsEnabled, "restored editor not usable");
            await reopened.LockAsync(); Invoke(main, "ReleaseSettledSession");
            SetField(main, "confirmedExit", true); main.Close(); main = null;
        }
        finally
        {
            if (main is not null) { SetField(main, "confirmedExit", true); main.Close(); var s = Field<SaveCoordinator?>(main, "session"); if (s is not null && !s.IsBusy) s.Dispose(); }
            CryptographicOperations.ZeroMemory(secret); if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void EditText(TextBox box, string text) { box.SelectAll(); box.SelectedText = text; }
    private static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);while(!ready()){if(DateTime.UtcNow>deadline)throw new Exception("WPF synthetic wait timed out");await Task.Delay(10);}await Idle();
    }
    private static T Control<T>(Window window, string name) where T : class => (window.FindName(name) as T) ?? throw new Exception("Missing WPF control " + name);
    private static T Field<T>(object target, string name) => (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target))!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

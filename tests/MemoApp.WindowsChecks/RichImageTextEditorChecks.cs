using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task RichImageTextEditorRun()
    {
        foreach(string scenario in new[]{"v1-constructor","capture-document","capture-awayback","preapply-document","preapply-awayback","preapply-native-text","generic-baseline","unavailable-font","fallback-epoch","fallback-metadata","fallback-hide","fallback-setter","edit-undo","edge-edit","composition","zero-images","ordinary-label","format","checklist","save-reopen","adjacent-delete","no-change","image-touch","clone","reorder","delete","extra-inline","active-link","embedded-ui","native-limit","source-invalidation","metadata-invalidation","paste-reentry","paste-selection","own-reentry","own-retire","notification-throw","queued-composition","setter","hide","lock"})await RichImageTextEditorCase(scenario);
    }
    private static string[] ImageRawNodes(StyledDocument document)
    {
        using var json=JsonDocument.Parse(document.SourceJson);return json.RootElement.GetProperty("nodes").EnumerateArray().Where(node=>node.GetProperty("type").GetString()=="image").Select(node=>node.GetRawText()).ToArray();
    }
    private static StructuredNoteEditor CreateSessionRichImageEditor(SaveCoordinator session,NoteDraft note,Func<bool> current)
    {
        // Frozen baseline deliberately reaches its real readonly v2 behavior, rather than
        // treating a missing new constructor as native product RED.
        var constructor=typeof(StructuredNoteEditor).GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[typeof(SaveCoordinator),typeof(NoteDraft),typeof(Func<bool>),typeof(Action<string>)],null);
        return constructor is null?new(session.Workspace,note,current,_=>{}):(StructuredNoteEditor)constructor.Invoke([session,note,current,new Action<string>(_=>{})]);
    }
    private static async Task RichImageTextEditorCase(string scenario)
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-v2-text-editor-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();SaveCoordinator? session=null;StructuredNoteEditor? editor=null;Window? window=null;bool live=true;Action? currentCallback=null;bool currentArmed=false;Action<NoteDraft?>? invalidating=null;Action? changed=null;DependencyPropertyDescriptor? descriptor=null;EventHandler? setter=null;TextComposition? activeComposition=null;var nativeCleanup=new List<Action>();Exception? primaryFailure=null;
        try
        {
            session=new(EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();session.Workspace.ConvertMode(note,"rich",true);
            session.Workspace.SetRichDocument(note,new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"before\"}]},{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"cell\"}]}]]},{\"type\":\"paragraph\",\"runs\":[{\"text\":\"after\"}]}]}"));
            if(scenario=="v1-constructor")
            {
                Require(await session.SaveAsync(),"Synthetic native v1 constructor baseline saved");string before=note.Document!.SourceJson;long version=note.EditVersion,epoch=session.AttachmentPreviewEpoch;byte[] baselineCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
                try
                {
                    var constructor=typeof(StructuredNoteEditor).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(SaveCoordinator),typeof(NoteDraft),typeof(Func<bool>),typeof(Action<string>)],null);Require(constructor is not null,"Closed session constructor is available");bool refused=false;
                    try{editor=(StructuredNoteEditor)constructor!.Invoke([session,note,new Func<bool>(()=>live),new Action<string>(_=>{})]);}catch(TargetInvocationException failure)when(failure.InnerException is InvalidOperationException){refused=true;}
                    Require(refused&&note.Document.SourceJson==before&&note.EditVersion==version&&session.AttachmentPreviewEpoch==epoch,"Internal session constructor rejects v1 before source/UI owner side effects");
                    editor=new StructuredNoteEditor(session.Workspace,note,()=>live,_=>{});window=new Window{Content=editor,Width=600,Height=500};window.Show();await Idle();Require(!editor.RichInput.IsReadOnly&&editor.RichInput.IsUndoEnabled&&note.Document.SourceJson==before&&baselineCipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Public generic v1 route remains genuinely editable with unchanged source/cipher baseline");return;
                }
                finally{CryptographicOperations.ZeroMemory(baselineCipher);}
            }
            if(scenario=="unavailable-font"||scenario.StartsWith("fallback-",StringComparison.Ordinal))session.Workspace.SetRichDocument(note,new(1,note.Document!.SourceJson.Replace("\"before\"","\"before\",\"fontFamily\":\"MemoApp Synthetic Missing Font 31DAD069\"",StringComparison.Ordinal)));
            Require(await session.PrepareAttachmentsAsync(),"Native v2 text editor synthetic root");Guid id=session.AttachBytes(note,PreviewPng,"editor.png","image/png",note.EditVersion);Require(await session.InsertInlineImageAsync(note,id,scenario=="edge-edit"?0:1,note.EditVersion)&&await session.InsertInlineImageAsync(note,id,scenario=="edge-edit"?4:3,note.EditVersion)&&await session.SaveAsync(),"Native v2 text fixture committed repeated original images");
            if(scenario=="zero-images"){session.RemoveInlineImage(note,3,note.EditVersion);session.RemoveInlineImage(note,1,note.EditVersion);Require(await session.SaveAsync()&&note.Document!.SchemaVersion==2,"Zero-image editor remains canonical v2");}
            string original=note.Document!.SourceJson;var originalImages=RichDocumentCodec.Images(note.Document).Select(i=>(i.AttachmentId,i.Alt)).ToArray();string[] originalRawImages=ImageRawNodes(note.Document);byte[] cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try
            {
                editor=scenario=="generic-baseline"?new StructuredNoteEditor(session.Workspace,note,()=>live,_=>{}):CreateSessionRichImageEditor(session,note,()=>{if(currentArmed){currentArmed=false;currentCallback?.Invoke();}return live;});window=new Window{Content=editor,Width=600,Height=500};window.Show();await Idle();
                if(scenario is "generic-baseline" or "unavailable-font"||scenario.StartsWith("fallback-",StringComparison.Ordinal))
                {
                    Require(editor.RichInput.IsReadOnly&&!editor.RichInput.IsUndoEnabled,"Generic v2 or unavailable-font whole source remains readonly with no native Undo authority");
                    editor.ApplyBold();editor.PasteData(new DataObject(DataFormats.UnicodeText,"generic refusal"));await Idle();
                    Require(note.Document.SourceJson==original&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Generic v2 route cannot publish text or save ciphertext");
                    if(scenario.StartsWith("fallback-",StringComparison.Ordinal))
                    {
                        if(scenario=="fallback-epoch")session.Workspace.AcceptPrepared(session.Workspace.Capture());
                        else if(scenario=="fallback-metadata")note.Title="synthetic fallback metadata change";
                        else if(scenario=="fallback-hide")window.Hide();
                        else
                        {
                            editor.RichInput.IsReadOnly=false;bool fired=false;descriptor=DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.RichTextBox.IsReadOnlyProperty,typeof(System.Windows.Controls.RichTextBox));setter=(_,_)=>{if(!fired){fired=true;note.Title="synthetic fallback setter metadata";}};descriptor.AddValueChanged(editor.RichInput,setter);Invoke(editor,"Rebuild");await Idle();Require(fired,"Readonly fallback rebuild fixture reaches a real native IsReadOnly setter callback");
                        }
                        Require(editor.IsDisposed&&editor.RichInput.Document.Blocks.Count==0&&!editor.RichInput.CanUndo&&note.Document.SourceJson==original,"Context-null session v2 fallback immediately retires on epoch/metadata/hide/native-setter changes rather than adopting newer source");
                    }
                    return;
                }
                Require(!editor.RichInput.IsReadOnly&&editor.RichInput.IsUndoEnabled,"Native product session-bound v2 text editor is writable without dropping original image blocks");
                var native=editor.RichInput;void ImagesExact()=>Require(note.Document!.SchemaVersion==2&&RichDocumentCodec.Images(note.Document).Select(i=>(i.AttachmentId,i.Alt)).SequenceEqual(originalImages)&&ImageRawNodes(note.Document).SequenceEqual(originalRawImages)&&note.AttachmentIds.Contains(id),"Actual v2 text edit preserves image IDs/alt/multiplicity/order and attachment membership");
                if(scenario.StartsWith("capture-",StringComparison.Ordinal)||scenario.StartsWith("preapply-",StringComparison.Ordinal))
                {
                    // Temporarily detach only the automatic commit handler while staging a real
                    // native candidate. The fixed native mutation bridge remains installed.
                    var ownChanged=(System.Windows.Controls.TextChangedEventHandler)typeof(StructuredNoteEditor).GetMethod("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate(typeof(System.Windows.Controls.TextChangedEventHandler),editor);
                    native.TextChanged-=ownChanged;try{((Run)((Paragraph)native.Document.Blocks.FirstBlock).Inlines.FirstInline).Text+=" actual staged candidate";}finally{native.TextChanged+=ownChanged;}
                    Require(note.Document.SourceJson==original,"Staged actual native candidate has not yet published source");var originalGraph=native.Document;int observedEvents=0;System.Windows.Controls.TextChangedEventHandler observed=(_,_)=>observedEvents++;native.TextChanged+=observed;nativeCleanup.Add(()=>native.TextChanged-=observed);
                    bool fired=false,swapped=false,restored=false;void Reenter()
                    {
                        if(fired)return;fired=true;
                        if(scenario=="preapply-native-text"){((Run)((Paragraph)native.Document.Blocks.FirstBlock).Inlines.FirstInline).Text+=" nested native mutation";return;}
                        var replacement=new FlowDocument(new Paragraph(new Run("synthetic replacement graph")));native.Document=replacement;swapped=ReferenceEquals(native.Document,replacement);
                        if(scenario.EndsWith("awayback",StringComparison.Ordinal)){native.Document=originalGraph;restored=ReferenceEquals(native.Document,originalGraph);}
                    }
                    if(scenario.StartsWith("capture-",StringComparison.Ordinal)){currentCallback=Reenter;currentArmed=true;}
                    else{invalidating=_=>Reenter();session.Workspace.AttachmentReadInvalidating+=invalidating;}
                    Invoke(editor,"CommitNative",false);await Idle();
                    Require(fired&&observedEvents>0&&(scenario=="preapply-native-text"||swapped)&&(!scenario.EndsWith("awayback",StringComparison.Ordinal)||restored),"Actual WPF callback really mutated/replaced/restored native graph while capture/publication was in flight");
                    Require(note.Document.SourceJson==original&&editor.IsDisposed&&!native.IsUndoEnabled&&!native.CanUndo&&originalGraph.Blocks.Count==0&&new TextRange(originalGraph.ContentStart,originalGraph.ContentEnd).Text.Length==0&&native.Document.Blocks.Count==0&&new TextRange(native.Document.ContentStart,native.Document.ContentEnd).Text.Length==0,"In-flight native graph mutation retires context before stale publication and clears both detached original and current native graphs even after callback reattachment");ImagesExact();
                }
                else if(scenario is "edit-undo" or "edge-edit" or "zero-images" or "ordinary-label")
                {
                    string typedText=scenario=="ordinary-label"?"[이미지: editor.png]":" 한글 e\u0301 😀";
                    var paragraph=native.Document.Blocks.OfType<Paragraph>().First(p=>new TextRange(p.ContentStart,p.ContentEnd).Text.StartsWith("before",StringComparison.Ordinal));native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Text=typedText;await Idle();ImagesExact();Require(note.Text.Contains(typedText,StringComparison.Ordinal)&&note.Document.SourceJson!=original,"Native typing actually publishes complete rich v2 source without interpreting equal image-label text as authority");
                    ApplicationCommands.Undo.Execute(null,native);await Idle();ImagesExact();Require(note.Document.SourceJson==original,"Native v2 Undo restores exact original owned JSON bytes");ApplicationCommands.Redo.Execute(null,native);await Idle();ImagesExact();Require(note.Text.Contains(typedText,StringComparison.Ordinal),"Actual v2 Redo republishes surrounding text");
                    var table=native.Document.Blocks.OfType<Table>().Single();var cell=(Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock;native.Selection.Select(cell.ContentStart,cell.ContentEnd);native.Selection.Text="edited cell";await Idle();ImagesExact();Require(note.Text.Contains("edited cell",StringComparison.Ordinal),"Actual existing table text edit preserves inline PNG blocks");
                }
                else if(scenario=="composition")
                {
                    window.Activate();native.Focus();Keyboard.Focus(native);await Idle();Require(native.IsKeyboardFocused,"Product composition fixture has actual native keyboard focus");
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);
                    activeComposition=new TextComposition(InputManager.Current,native,"한글 e\u0301 😀",TextCompositionAutoComplete.Off);
                    int starts=0,updates=0,finishes=0;TextCompositionEventHandler start=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))starts++;};TextCompositionEventHandler update=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))updates++;};TextCompositionEventHandler finish=(_,e)=>{if(ReferenceEquals(e.TextComposition,activeComposition))finishes++;};
                    native.AddHandler(TextCompositionManager.PreviewTextInputStartEvent,start,true);nativeCleanup.Add(()=>native.RemoveHandler(TextCompositionManager.PreviewTextInputStartEvent,start));native.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent,update,true);nativeCleanup.Add(()=>native.RemoveHandler(TextCompositionManager.PreviewTextInputUpdateEvent,update));native.AddHandler(TextCompositionManager.PreviewTextInputEvent,finish,true);nativeCleanup.Add(()=>native.RemoveHandler(TextCompositionManager.PreviewTextInputEvent,finish));
                    TextCompositionManager.StartComposition(activeComposition);Require(starts==1&&updates==0&&finishes==0&&note.Document.SourceJson==original,"Product composition start cannot prematurely publish native text");
                    TextCompositionManager.UpdateComposition(activeComposition);Require(starts==1&&updates==1&&finishes==0&&note.Document.SourceJson==original,"Product composition update cannot prematurely publish native text");
                    TextCompositionManager.CompleteComposition(activeComposition);activeComposition=null;await Idle();ImagesExact();
                    Require(starts==1&&updates==1&&finishes==1&&note.Text.Contains("한글 e\u0301 😀",StringComparison.Ordinal)&&note.Document.SourceJson!=original,"Actual TextCompositionManager completion inserts Unicode and publishes product v2 text");
                    ApplicationCommands.Undo.Execute(null,native);await Idle();Require(note.Document.SourceJson==original,"Completed product composition Undo restores exact original source");ImagesExact();
                }
                else if(scenario=="format")
                {
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
                    editor.ApplyBold();await Idle();editor.ApplyUnderline();await Idle();editor.ApplyForeground("#123456");await Idle();ImagesExact();
                    using var styled=JsonDocument.Parse(note.Document.SourceJson);var run=styled.RootElement.GetProperty("nodes")[0].GetProperty("runs")[0];
                    Require(run.GetProperty("bold").GetBoolean()&&run.GetProperty("underline").GetBoolean()&&run.GetProperty("foreground").GetString()=="#FF123456","Native surrounding-text styles publish actual canonical v2 runs");
                }
                else if(scenario=="checklist")
                {
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentStart,paragraph.ContentStart);editor.InsertChecklist();await Idle();ImagesExact();
                    editor.ToggleChecked();await Idle();ImagesExact();using var checkedSource=JsonDocument.Parse(note.Document.SourceJson);
                    var block=checkedSource.RootElement.GetProperty("nodes")[0];Require(block.GetProperty("type").GetString()=="checklist"&&block.GetProperty("items")[0].GetProperty("checked").GetBoolean(),"Actual native checklist edit and toggle publish complete v2 around unchanged images");
                }
                else if(scenario=="save-reopen")
                {
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Text=" saved v2 text";await Idle();ImagesExact();
                    Require(cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Native edit remains unsaved until explicit save");string candidate=note.Document.SourceJson;Guid noteId=note.Id;
                    editor.Dispose();window.Content=null;Require(await session.SaveAsync()&&!session.IsDirty,"Explicit normal save settles edited v2 source");Require(await session.LockAsync()&&session.KeysReleased&&!session.IsBusy,"Saved v2 native fixture truly locks before reopen");session.Dispose();session=null;
                    session=new(EncryptedVault.Open(root,secret),TimeProvider.System);var reopened=session.Workspace.Notes.Single(n=>n.Id==noteId);
                    Require(reopened.Document!.SchemaVersion==2&&reopened.Document.SourceJson==candidate&&ImageRawNodes(reopened.Document).SequenceEqual(originalRawImages)&&reopened.AttachmentIds.Contains(id),"Actual encrypted save/reopen retains complete edited source and exact repeated inline-image records");
                }
                else if(scenario=="adjacent-delete")
                {
                    string before=note.Document.SourceJson;var after=(Paragraph)native.Document.Blocks.LastBlock;native.Selection.Select(after.ContentStart,after.ContentStart);
                    EditingCommands.Backspace.Execute(null,native);await Idle();Require(note.Document.SourceJson==before&&native.Document.Blocks.Count==5,"Native Backspace at following paragraph boundary cannot merge or remove original image");
                    var first=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(first.ContentEnd,first.ContentEnd);EditingCommands.Delete.Execute(null,native);await Idle();
                    Require(note.Document.SourceJson==before&&native.Document.Blocks.Count==5,"Native Delete at preceding paragraph boundary cannot merge or remove original image");ImagesExact();
                }
                else if(scenario=="no-change")
                {
                    long version=note.EditVersion,content=note.ContentVersion,epoch=session.AttachmentPreviewEpoch;Invoke(editor,"CommitNative",false);await Idle();
                    Require(note.Document.SourceJson==original&&note.EditVersion==version&&note.ContentVersion==content&&session.AttachmentPreviewEpoch==epoch&&!session.IsDirty,"Native no-change capture does not mint edit/history/epoch or dirty state");ImagesExact();
                }
                else if(scenario is "image-touch" or "clone" or "reorder" or "delete" or "extra-inline" or "active-link" or "embedded-ui" or "native-limit")
                {
                    var image=(Paragraph)native.Document.Blocks.ElementAt(RichDocumentCodec.Images(note.Document).First().BlockIndex);string before=note.Document.SourceJson;
                    if(scenario=="image-touch"){native.Selection.Select(image.ContentStart,image.ContentEnd);editor.ApplyBold();editor.PasteData(new DataObject(DataFormats.UnicodeText,"forbidden replacement"));EditingCommands.EnterParagraphBreak.Execute(null,native);EditingCommands.Delete.Execute(null,native);EditingCommands.Backspace.Execute(null,native);ApplicationCommands.Cut.Execute(null,native);await Idle();Require(note.Document.SourceJson==before,"Placeholder-touch formatting and paste refuse whole edit");}
                    native.BeginChange();try{if(scenario=="clone"){var clone=new Paragraph(new Run(new TextRange(image.ContentStart,image.ContentEnd).Text.TrimEnd('\r','\n'))){Tag=id};native.Document.Blocks.InsertBefore(image,clone);native.Document.Blocks.Remove(image);}else if(scenario=="delete")native.Document.Blocks.Remove(image);else if(scenario=="reorder"){var last=(Paragraph)native.Document.Blocks.ElementAt(RichDocumentCodec.Images(note.Document).Last().BlockIndex);native.Document.Blocks.Remove(last);native.Document.Blocks.InsertBefore(image,last);}else if(scenario=="extra-inline")image.Inlines.Add(new Run("unauthorized inline"));else if(scenario=="active-link")((Paragraph)native.Document.Blocks.FirstBlock).Inlines.Add(new Hyperlink(new Run("active")));else if(scenario=="embedded-ui")((Paragraph)native.Document.Blocks.FirstBlock).Inlines.Add(new InlineUIContainer(new System.Windows.Controls.TextBlock{Text="synthetic embedded UI"}));else if(scenario=="native-limit")((Run)((Paragraph)native.Document.Blocks.FirstBlock).Inlines.FirstInline).Text=new string('x',RichDocumentCodec.MaxText+1);else ((Run)image.Inlines.FirstInline).Text="forged image label";}finally{native.EndChange();}await Idle();Require(note.Document.SourceJson==before&&!native.CanUndo,"Direct native image mutation/clone/deletion/repeated-identity reordering is refused and invalid native Undo discarded");ImagesExact();
                }
                else if(scenario=="source-invalidation")
                {
                    session.Workspace.AcceptPrepared(session.Workspace.Capture());Require(editor.IsDisposed&&native.Document.Blocks.Count==0&&!native.CanUndo,"Same-version AcceptPrepared immediately clears native v2 source/Undo and retires exact context");
                }
                else if(scenario=="metadata-invalidation")
                {
                    note.Title="synthetic external metadata";Require(editor.IsDisposed&&native.Document.Blocks.Count==0&&!native.CanUndo&&note.Document.SourceJson==original,"External metadata edit immediately retires original v2 text source authority");
                }
                else if(scenario=="paste-reentry")
                {
                    bool fired=false;editor.PasteData(new DelayedTextData(()=>{fired=true;note.Title="synthetic paste-time metadata";}));await Idle();
                    Require(fired&&editor.IsDisposed&&note.Document.SourceJson==original&&note.Title=="synthetic paste-time metadata"&&native.Document.Blocks.Count==0,"Actual IDataObject read callback cannot paste after original source authority changes");
                }
                else if(scenario=="paste-selection")
                {
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentStart,paragraph.ContentStart);bool fired=false;
                    editor.PasteData(new DelayedTextData(()=>{fired=true;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Select(paragraph.ContentStart,paragraph.ContentStart);}));await Idle();
                    Require(fired&&!editor.IsDisposed&&note.Document.SourceJson==original,"Actual native selection-away/back during IDataObject read monotonically refuses stale paste even with identical final caret");ImagesExact();
                }
                else if(scenario=="lock")
                {
                    Require(await session.LockAsync()&&session.KeysReleased&&!session.IsBusy,"Actual session lock reaches true settlement");Require(editor.IsDisposed&&native.Document.Blocks.Count==0&&!native.CanUndo,"Actual Conceal retires v2 context and clears native source/Undo immediately");
                }
                else if(scenario=="own-reentry")
                {
                    bool fired=false;invalidating=_=>{if(!fired){fired=true;note.Title="explicit nested edit";}};session.Workspace.AttachmentReadInvalidating+=invalidating;
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Text="stale candidate";await Idle();Require(fired&&note.Title=="explicit nested edit"&&note.Document.SourceJson==original,"Nested prepublication source edit retained while stale v2 native candidate refuses before install");
                }
                else if(scenario=="own-retire")
                {
                    bool fired=false;invalidating=_=>{if(!fired){fired=true;live=false;editor.ClearSensitive();}};session.Workspace.AttachmentReadInvalidating+=invalidating;
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Text="candidate retired before install";await Idle();
                    Require(fired&&note.Document.SourceJson==original&&editor.IsDisposed&&native.Document.Blocks.Count==0,"Exact host retirement during own prepublication callback forbids source installation and continuation");
                }
                else if(scenario=="notification-throw")
                {
                    bool fired=false;changed=()=>{if(!fired){fired=true;throw new InvalidOperationException("SYNTHETIC_V2_POST_INSTALL_NOTIFICATION");}};session.Workspace.Changed+=changed;
                    var paragraph=(Paragraph)native.Document.Blocks.FirstBlock;native.Selection.Select(paragraph.ContentEnd,paragraph.ContentEnd);native.Selection.Text="valid applied candidate";await Idle();ImagesExact();Require(fired&&note.Text.Contains("valid applied candidate",StringComparison.Ordinal)&&session.IsDirty&&editor.IsDisposed&&native.Document.Blocks.Count==0,"AppliedDirty receipt retains exact valid mutation after post-install observer failure while retiring native authority");
                }
                else if(scenario=="queued-composition")
                {
                    var composition=new TextComposition(InputManager.Current,native,"late synthetic",TextCompositionAutoComplete.Off);TextCompositionManager.StartComposition(composition);TextCompositionManager.CompleteComposition(composition);live=false;editor.ClearSensitive();await Idle();Require(note.Document.SourceJson==original&&native.Document.Blocks.Count==0&&!native.CanUndo,"Queued native composition completion cannot revive retired host/source");
                }
                else if(scenario=="hide")
                {
                    window.Hide();Require(editor.IsDisposed&&native.Document.Blocks.Count==0&&!native.CanUndo&&note.Document.SourceJson==original,"Actual native hide clears text/Undo and original editor authority");
                }
                else
                {
                    native.IsReadOnly=true;bool fired=false;descriptor=DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.RichTextBox.IsReadOnlyProperty,typeof(System.Windows.Controls.RichTextBox));setter=(_,_)=>{if(!fired){fired=true;live=false;editor.ClearSensitive();}};descriptor.AddValueChanged(native,setter);Invoke(editor,"Rebuild");await Idle();Require(fired&&editor.IsDisposed&&native.Document.Blocks.Count==0&&note.Document.SourceJson==original,"Reentrant actual IsReadOnly publication setter cannot resurrect v2 source");
                }
                Require(scenario=="save-reopen"||cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Native v2 editor makes no implicit ciphertext save or root/object rewrite");
            }
            finally{CryptographicOperations.ZeroMemory(cipher);}
        }
        catch(Exception error){primaryFailure=error;throw;}
        finally
        {
            var failures=new List<Exception>();foreach(Action cleanup in new Action[]{()=>{if(activeComposition is not null)TextCompositionManager.CompleteComposition(activeComposition);},()=>{if(descriptor is not null&&setter is not null&&editor is not null)descriptor.RemoveValueChanged(editor.RichInput,setter);},()=>{if(invalidating is not null&&session is not null)session.Workspace.AttachmentReadInvalidating-=invalidating;},()=>{if(changed is not null&&session is not null)session.Workspace.Changed-=changed;},()=>{live=false;editor?.Dispose();},()=>{if(window is not null)window.Content=null;},()=>window?.Close()}.Concat(nativeCleanup))try{cleanup();}catch(Exception error){failures.Add(error);}
            bool cleaned=session is null;try{if(session is not null){try{await session.LockAsync();}catch(Exception error){failures.Add(error);}if(session.KeysReleased&&!session.IsBusy){try{session.Dispose();cleaned=true;}catch(Exception error){failures.Add(error);}}}}finally{CryptographicOperations.ZeroMemory(secret);if(cleaned&&Directory.Exists(root))try{Directory.Delete(root,true);}catch(Exception error){failures.Add(error);}}
            if(failures.Count!=0){if(primaryFailure is not null)failures.Insert(0,primaryFailure);throw new AggregateException("Synthetic v2 editor cleanup failed",failures);}
        }
    }
}

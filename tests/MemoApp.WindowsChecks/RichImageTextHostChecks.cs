using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;

internal static partial class Program
{
    // Native integration contract for the actual Main and Sticky composite hosts.
    private static async Task RichImageTextHostRun()
    {
        foreach(string scenario in new[]{"main-table","sticky-table","move","cross-host","nested-document","nested-sticky-document","nested-workspace","nested-foreign-before","nested-foreign-after","selection-awayback","selection-loading","fold","hide","epoch","metadata","readonly-fallback","old-buttons","pending-display","setter","setter-source","setter-conceal","child-content-awayback","child-editor-awayback","main-context-awayback","sticky-context-awayback","generic-native-refresh","lock"})
            {Console.WriteLine("H01 host native CASE "+scenario);await RichImageTextHostCase(scenario);}
    }
    private sealed record RichHostWitness(object Owner,StructuredNoteEditor Editor,RichImageDocumentView Images,FlowDocument Graph,object[] Actions);
    private static T RichHostProperty<T>(object owner,string name)=>(T)owner.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner)!;
    private static RichHostWitness RichHost(Window window)
    {
        object? owner=Control<ContentControl>(window,"StructuredHost").Content;
        Require(owner is not null&&owner.GetType().Name=="RichImageTextHost","Actual selected canonical v2 host must combine editable text and explicit image actions; existing readonly product baseline cannot satisfy this behavior");
        var editor=RichHostProperty<StructuredNoteEditor>(owner!,"Editor");var images=RichHostProperty<RichImageDocumentView>(owner!,"ImageView");
        return new(owner!,editor,images,editor.RichInput.Document,Field<System.Collections.IDictionary>(images,"renderedActions").Values.Cast<object>().ToArray());
    }
    private static void RichHostRetired(RichHostWitness witness,Image[] images)
    {
        Require(witness.Actions.All(action=>Field<object?>(action,"Note") is null&&Field<object?>(action,"Document") is null&&Field<string?>(action,"Alt") is null&&Field<RoutedEventHandler?>(action,"DisplayHandler") is null&&Field<RoutedEventHandler?>(action,"RemoveHandler") is null&&Field<StackPanel>(action,"Panel").Children.OfType<TextBlock>().All(text=>text.Text.Length==0)&&Field<Image>(action,"Image").Source is null),"Retired rendered-action snapshots drop exact note/document/alt references even when native buttons are retained");
        Require(RichHostProperty<bool>(witness.Owner,"IsDisposed")&&witness.Editor.IsDisposed&&witness.Images.IsDisposed&&witness.Graph.Blocks.Count==0&&new TextRange(witness.Graph.ContentStart,witness.Graph.ContentEnd).Text.Length==0&&!witness.Editor.RichInput.IsUndoEnabled&&!witness.Editor.RichInput.CanUndo&&images.All(image=>image.Source is null),"Retired composite clears every child, exact owned text graph/Undo and retained pixel references");
    }
    private static bool RichNativeWalkBusy()=>(bool)typeof(MainWindow).Assembly.GetType("MemoApp.Windows.RichImageTextPhase")!.GetMethod("NativeWalkBusy",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Dispatcher.CurrentDispatcher])!;
    private static void RichHostStructureRetired(RichHostWitness witness)
    {
        Require(Field<DockPanel>(witness.Owner,"root").Children.Count==0&&witness.Images.BlocksHost.Children.Count==0&&witness.Actions.All(action=>Field<StackPanel>(action,"Panel").Children.Count==0),"After actual native unwind/Idle all exact retired panel structures are detached");
    }
    private static void PumpRichHostNotifications()
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
    }
    private sealed class CountingRichHostBackend:ImagePreviewBackend
    {
        internal int Decodes;
        internal override OwnedBgraRaster Decode(AttachmentReadLease lease,CancellationToken token)
        {Interlocked.Increment(ref Decodes);return base.Decode(lease,token);}
    }
    private static Button RichHostButton(RichImageDocumentView view,string label)
    {
        return view.BlocksHost.Children.OfType<StackPanel>().SelectMany(panel=>panel.Children.OfType<WrapPanel>()).SelectMany(panel=>panel.Children.OfType<Button>()).First(button=>Equals(button.Content,label));
    }
    private static async Task RichImageTextHostCase(string scenario)
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-v2-host-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[]? cipher=null;
        MainWindow? main=null;Window? extraWindow=null;RichImageDocumentView? extraView=null;SaveCoordinator? session=null;PausedImageBackend? paused=null;var pending=new List<Task<bool>>();var detach=new List<Action>();Exception? primary=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));session=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var note=session.Workspace.CreateNote();session.Workspace.ConvertMode(note,"rich",true);
            string font=scenario=="readonly-fallback"?",\"fontFamily\":\"MemoApp Host Missing Font 62080B1B\"":"";
            session.Workspace.SetRichDocument(note,new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"before\""+font+"}]},{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"cell\"}]}]]},{\"type\":\"paragraph\",\"runs\":[{\"text\":\"after\"}]}]}"));
            Require(await session.PrepareAttachmentsAsync(),"Host fixture authenticated synthetic root");Guid id=session.AttachBytes(note,PreviewPng,"host.png","image/png",note.EditVersion);Require(await session.InsertInlineImageAsync(note,id,1,note.EditVersion)&&await session.SaveAsync(),"Host fixture anchored known v2 source");
            var other=session.Workspace.CreateNote();other.Title="later synthetic note";other.Text="other";Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");
            Invoke(main,"OpenSticky",note);await Idle();await Field<Task>(main,"recentTask");Require(await session.SaveAsync(),"Host baseline includes actual sticky placement/recent state");await Idle();
            var sticky=Field<Dictionary<Guid,StickyNoteWindow>>(main,"stickyWindows")[note.Id];var mainHost=RichHost(main);var stickyHost=RichHost(sticky);
            int block=RichDocumentCodec.Images(note.Document!).Single().BlockIndex;var mainPixels=mainHost.Images.ImageForBlock(block);var stickyPixels=stickyHost.Images.ImageForBlock(block);
            Require(mainPixels.Source is null&&stickyPixels.Source is null&&!System.Windows.Data.BindingOperations.IsDataBound(Control<TextBox>(main,"BodyEditor"),TextBox.TextProperty)&&!System.Windows.Data.BindingOperations.IsDataBound(Control<TextBox>(sticky,"BodyEditor"),TextBox.TextProperty),"Opening composite never paints image pixels or enables lossy plain-text binding");
            string original=note.Document!.SourceJson;string[] rawImages=ImageRawNodes(note.Document);long originalVersion=note.EditVersion,originalContentVersion=note.ContentVersion,originalEpoch=session.AttachmentPreviewEpoch;cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            var list=Control<ListBox>(main,"NotesList");Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItems[0],note),"Actual native selected-note baseline is exact singleton");
            if(scenario=="readonly-fallback")
            {
                Require(mainHost.Editor.RichInput.IsReadOnly&&stickyHost.Editor.RichInput.IsReadOnly,"Unavailable native font gives whole readonly fallback in both hosts");note.Title="external fallback metadata";Require(mainHost.Editor.IsDisposed&&stickyHost.Editor.IsDisposed,"Context-null fallback cannot adopt changed source");await Idle();
                Require(!ReferenceEquals(RichHost(main).Owner,mainHost.Owner)&&!ReferenceEquals(RichHost(sticky).Owner,stickyHost.Owner)&&RichHost(main).Editor.RichInput.IsReadOnly&&note.Document.SourceJson==original,"Fallback refresh creates fresh owner and retains exact original readonly source");
            }
            else
            {
                Require(!mainHost.Editor.RichInput.IsReadOnly&&!stickyHost.Editor.RichInput.IsReadOnly,"Actual Main and Sticky session-bound v2 editors are writable");
                if(scenario is "main-table" or "sticky-table" or "cross-host" or "move" or "nested-document" or "nested-sticky-document" or "nested-workspace" or "nested-foreign-before" or "nested-foreign-after")
                {
                    var editing=scenario is "sticky-table" or "nested-sticky-document"?stickyHost:mainHost;int moved=0,selectionEvents=0,pumps=0;bool fired=false;int beforeIndex=list.Items.IndexOf(note);
                    if(scenario=="move")
                    {
                        Require(beforeIndex>list.Items.IndexOf(other),"Modified-sort fixture starts selected note behind newer note");Require(list.ItemsSource is INotifyCollectionChanged,"Main result collection must support selection-preserving native Move");
                        var changes=(INotifyCollectionChanged)list.ItemsSource;NotifyCollectionChangedEventHandler movedHandler=(_,e)=>{if(e.Action==NotifyCollectionChangedAction.Move&&e.NewItems?.Contains(note)==true)moved++;};changes.CollectionChanged+=movedHandler;detach.Add(()=>changes.CollectionChanged-=movedHandler);
                        SelectionChangedEventHandler selectedHandler=(_,_)=>selectionEvents++;list.SelectionChanged+=selectedHandler;detach.Add(()=>list.SelectionChanged-=selectedHandler);
                    }
                    void PumpOwn()
                    {
                        if(fired)return;fired=true;pumps++;Require(RichHostProperty<bool>(editing.Editor,"IsRichImageTextRefreshDeferred"),"Real native/Core transaction exposes fixed in-flight state before owner refresh");PumpRichHostNotifications();
                        var editingWindow=scenario=="nested-sticky-document"?(Window)sticky:main;var otherWindow=scenario=="nested-sticky-document"?(Window)main:sticky;var otherOwner=scenario=="nested-sticky-document"?mainHost:stickyHost;Require(ReferenceEquals(RichHost(editingWindow).Owner,editing.Owner)&&!editing.Editor.IsDisposed&&(Control<ContentControl>(otherWindow,"StructuredHost").Content is null||ReferenceEquals(Control<ContentControl>(otherWindow,"StructuredHost").Content,otherOwner.Owner)),"Nested dispatcher pump before receipt retains exact own editor and forbids foreign-host fresh capture before shared transaction unwind");
                    }
                    if(scenario is "nested-document" or "nested-sticky-document"){PropertyChangedEventHandler listener=(_,e)=>{if(e.PropertyName==nameof(NoteDraft.Document))PumpOwn();};note.PropertyChanged+=listener;detach.Add(()=>note.PropertyChanged-=listener);}
                    if(scenario=="nested-workspace"){Action listener=PumpOwn;session.Workspace.Changed+=listener;detach.Add(()=>session.Workspace.Changed-=listener);}
                    if(scenario=="nested-foreign-before"){Action<NoteDraft?> listener=_=>{if(!fired){fired=true;note.Title="foreign before install";PumpRichHostNotifications();}};session.Workspace.AttachmentReadInvalidating+=listener;detach.Add(()=>session.Workspace.AttachmentReadInvalidating-=listener);}
                    if(scenario=="nested-foreign-after"){Action listener=()=>{if(!fired){fired=true;note.Title="foreign after install";PumpRichHostNotifications();}};session.Workspace.Changed+=listener;detach.Add(()=>session.Workspace.Changed-=listener);}
                    var table=editing.Editor.RichInput.Document.Blocks.OfType<Table>().Single();var cell=(Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock;editing.Editor.RichInput.Selection.Select(cell.ContentStart,cell.ContentEnd);editing.Editor.RichInput.Selection.Text="actual host table edit";
                    if(scenario.StartsWith("nested-foreign",StringComparison.Ordinal))
                    {
                        Require(fired&&editing.Editor.IsDisposed,"Foreign nested observer immediately retires original authority even while native/Core transaction remains unsettled");await Idle();
                        Require(note.Title==(scenario=="nested-foreign-before"?"foreign before install":"foreign after install")&&(scenario=="nested-foreign-before"?note.Document.SourceJson==original:note.Text.Contains("actual host table edit",StringComparison.Ordinal)),"Nested foreign mutation preserves honest pre-install refusal or post-install AppliedDirty without predecessor rollback");
                        Require(!ReferenceEquals(RichHost(main).Owner,mainHost.Owner)&&!ReferenceEquals(RichHost(sticky).Owner,stickyHost.Owner)&&!RichHost(main).Editor.RichInput.IsReadOnly,"After true unwind both foreign-retired hosts get fresh checked owners, not occupied-attempt readonly adoption");
                    }
                    else
                    {
                        Require((scenario is "sticky-table" or "nested-sticky-document"?mainHost:stickyHost).Editor.IsDisposed,"Editing one host immediately retires the other original source");await Idle();var retained=RichHost(scenario is "sticky-table" or "nested-sticky-document"?sticky:main);
                        Require(ReferenceEquals(retained.Owner,editing.Owner)&&ReferenceEquals(retained.Editor,editing.Editor)&&editing.Editor.RichInput.CanUndo&&note.Text.Contains("actual host table edit",StringComparison.Ordinal),"Own publication preserves exact editor/Undo through real host refresh");
                        Require(scenario is not ("nested-document" or "nested-sticky-document" or "nested-workspace")||pumps==1,"Required notification really entered nested native dispatcher frame");
                        if(scenario=="move")Require(moved>0&&list.Items.IndexOf(note)<beforeIndex&&selectionEvents==0&&list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItems[0],note),"Actual collection Move reorders selected result while preserving raw native selection and exact owner");
                        ApplicationCommands.Undo.Execute(null,editing.Editor.RichInput);await Idle();Require(note.Document.SourceJson==original,"Actual Main/Sticky native Undo restores exact original canonical source around image");ApplicationCommands.Redo.Execute(null,editing.Editor.RichInput);await Idle();Require(note.Text.Contains("actual host table edit",StringComparison.Ordinal),"Actual host Redo preserves original image authority");
                    }
                    Require(ImageRawNodes(note.Document!).SequenceEqual(rawImages)&&note.AttachmentIds.Contains(id),"Host transaction preserves exact canonical image nodes/attachments");
                }
                else if(scenario is "selection-awayback" or "selection-loading")
                {
                    bool loading=Field<bool>(main,"loadingUi");try{if(scenario=="selection-loading")SetField(main,"loadingUi",true);list.SelectedItem=other;list.SelectedItem=note;}finally{SetField(main,"loadingUi",loading);}RichHostRetired(mainHost,[mainPixels]);Invoke(main,"SelectEditor");await Idle();Require(!ReferenceEquals(RichHost(main).Owner,mainHost.Owner)&&note.Document.SourceJson==original,"True selection-away/back retires even during loadingUi and returning creates fresh owner");
                }
                else if(scenario is "fold" or "hide")
                {
                    if(scenario=="fold")Control<CheckBox>(sticky,"FoldToggle").IsChecked=true;else sticky.Hide();RichHostRetired(stickyHost,[stickyPixels]);
                    if(scenario=="fold")Control<CheckBox>(sticky,"FoldToggle").IsChecked=false;else sticky.Show();await Idle();Require(!ReferenceEquals(RichHost(sticky).Owner,stickyHost.Owner)&&note.Document.SourceJson==original,"Unfold/show creates fresh source owner without resurrecting retired text/Undo/pixels");
                }
                else if(scenario is "epoch" or "metadata")
                {
                    if(scenario=="epoch")session.Workspace.AcceptPrepared(session.Workspace.Capture());else note.Title="external host metadata";Require(mainHost.Editor.IsDisposed&&stickyHost.Editor.IsDisposed,"Same-version acceptance or metadata invalidates both editors synchronously");await Idle();RichHostRetired(mainHost,[mainPixels]);RichHostRetired(stickyHost,[stickyPixels]);Require(!ReferenceEquals(RichHost(main).Owner,mainHost.Owner)&&note.Document.SourceJson==original,"Fresh epoch/source owner retains exact source without old continuation adoption");
                }
                else if(scenario=="old-buttons")
                {
                    var spy=new CountingRichHostBackend();SetField(mainHost.Images,"backend",spy);var display=RichHostButton(mainHost.Images,"이미지 표시");var remove=RichHostButton(mainHost.Images,"이미지 블록 제거");
                    var native=mainHost.Editor.RichInput;main.Activate();native.Focus();Keyboard.Focus(native);await Idle();
                    Require(native.IsKeyboardFocused&&ReferenceEquals(RichHost(main).Editor,mainHost.Editor)&&mainHost.Editor.HasCurrentRichImageTextProjection,"Stale-button index-shift fixture uses actual focused original current editor");
                    var graph=native.Document;var first=(Paragraph)graph.Blocks.FirstBlock;var originalRun=(Run)first.Inlines.FirstInline;Require(originalRun.Text.Length>3,"Synthetic first run has a real interior split position");
                    var interior=originalRun.ContentStart.GetPositionAtOffset(3,LogicalDirection.Forward)!;native.Selection.Select(interior,interior);
                    bool exactCaret=native.Selection.IsEmpty&&ReferenceEquals(native.Selection.Start.Paragraph,first)&&native.Selection.Start.CompareTo(originalRun.ContentStart)>0&&native.Selection.Start.CompareTo(originalRun.ContentEnd)<0;
                    bool allowed=(bool)Invoke(mainHost.Editor,"ImageTextCommandAllowed")!;bool canExecute=EditingCommands.EnterParagraphBreak.CanExecute(null,native);
                    int beforeBlocks=graph.Blocks.Count,textEvents=0,commandEvents=0;bool commandHandled=false;long beforeVersion=note.EditVersion;var placeholder=graph.Blocks.Cast<Block>().ElementAt(block);
                    TextChangedEventHandler changed=(_,_)=>textEvents++;ExecutedRoutedEventHandler command=(_,e)=>{if(e.Command==EditingCommands.EnterParagraphBreak){commandEvents++;commandHandled=e.Handled;}};
                    native.TextChanged+=changed;native.AddHandler(CommandManager.PreviewExecutedEvent,command,true);detach.Add(()=>native.TextChanged-=changed);detach.Add(()=>native.RemoveHandler(CommandManager.PreviewExecutedEvent,command));
                    Console.WriteLine($"H01 host old-buttons before focused={native.IsKeyboardFocused} exactCaret={exactCaret} allowed={allowed} canExecute={canExecute} blocks={beforeBlocks} imageIndex={block}");
                    Require(exactCaret&&allowed&&canExecute,"Real ordinary-text interior caret is current, avoids original image placeholder, and permits the native paragraph-break command");
                    EditingCommands.EnterParagraphBreak.Execute(null,native);await Idle();int afterIndex=RichDocumentCodec.Images(note.Document!).Single().BlockIndex;
                    bool sameEditor=ReferenceEquals(RichHost(main).Editor,mainHost.Editor),sameGraph=ReferenceEquals(native.Document,graph),samePlaceholder=graph.Blocks.Cast<Block>().ElementAtOrDefault(afterIndex) is{ } retained&&ReferenceEquals(retained,placeholder);
                    Console.WriteLine($"H01 host old-buttons after textEvents={textEvents} commandEvents={commandEvents} handled={commandHandled} blocks={graph.Blocks.Count} blockDelta={graph.Blocks.Count-beforeBlocks} imageIndex={afterIndex} indexDelta={afterIndex-block} editDelta={note.EditVersion-beforeVersion} sourceChanged={note.Document.SourceJson!=original} sameEditor={sameEditor} sameGraph={sameGraph} samePlaceholder={samePlaceholder}");
                    Require(textEvents>0&&commandEvents==1&&sameEditor&&sameGraph&&samePlaceholder&&graph.Blocks.Count==beforeBlocks+1&&afterIndex==block+1&&note.EditVersion>beforeVersion&&note.Document.SourceJson!=original&&ImageRawNodes(note.Document).SequenceEqual(rawImages),"Actual interior native paragraph split adds exactly one block before the unchanged exact image placeholder and genuinely publishes shifted source/index/version");
                    string candidate=note.Document!.SourceJson;long version=note.EditVersion;
                    display.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle();await session.WhenAttachmentReadsIdle;
                    Require(mainHost.Actions.All(action=>Field<object?>(action,"Note") is null&&Field<object?>(action,"Document") is null&&Field<string?>(action,"Alt") is null&&Field<RoutedEventHandler?>(action,"DisplayHandler") is null&&Field<RoutedEventHandler?>(action,"RemoveHandler") is null&&Field<StackPanel>(action,"Panel").Children.Count==0&&Field<Image>(action,"Image").Source is null),"Old rendered buttons retain no canonical source/note/alt snapshot after refresh");
                    Require(spy.Decodes==0&&note.Document.SourceJson==candidate&&note.EditVersion==version&&mainPixels.Source is null,"Detached rendered buttons cannot read/remove a different current-index block after text shift");
                }
                else if(scenario=="pending-display")
                {
                    paused=new PausedImageBackend(true);SetField(mainHost.Images,"backend",paused);var task=mainHost.Images.DisplayImageAsync(block);pending.Add(task);Require(paused.Started.Wait(10000),"Actual explicit host display reaches native decoder barrier");note.Title="source changed during decode";await Idle();Require(!await RichHost(main).Images.DisplayImageAsync(block),"New source owner cannot steal global admission before old actual decoder settles");paused.Release.Set();Require(!await task&&paused.Output is not null&&paused.Output.All(value=>value==0)&&mainPixels.Source is null,"Late owned raster is rejected/zeroed after original host revocation and actual worker settlement");
                }
                else if(scenario is "main-context-awayback" or "sticky-context-awayback")
                {
                    bool isSticky=scenario=="sticky-context-awayback";var retired=isSticky?stickyHost:mainHost;var pixel=isSticky?stickyPixels:mainPixels;
                    var oldCurrent=Field<Func<bool>>(retired.Editor,"current");Require(await retired.Images.DisplayImageAsync(block)&&pixel.Source is not null,"DataContext fixture publishes pixels only through actual explicit native display");
                    FrameworkElement contextHost=isSticky?sticky:Control<FrameworkElement>(main,"Editor");var mount=Control<ContentControl>(isSticky?(Window)sticky:main,"StructuredHost");var oldRoot=Field<DockPanel>(retired.Owner,"root");var replacement=new TextBlock{Text="synthetic retained replacement"};bool walked=false;
                    DependencyPropertyChangedEventHandler walking=(_,e)=>
                    {
                        if(walked||!ReferenceEquals(e.NewValue,other))return;walked=true;Require(RichNativeWalkBusy()&&!oldCurrent(),"Actual inherited DataContext callback is inside fixed native walk after immediate irreversible authority retirement");RichHostRetired(retired,[pixel]);int count=oldRoot.Children.Count;Require(count==2,"Immediate redaction preserves visual child count until native enumeration unwinds");
                        PumpRichHostNotifications();Require(RichNativeWalkBusy()&&oldRoot.Children.Count==count&&ReferenceEquals(mount.Content,retired.Owner),"Nested dispatcher pump inside real DataContext walk cannot structurally detach or create a fresh owner");mount.Content=replacement;
                    };
                    contextHost.DataContextChanged+=walking;detach.Add(()=>contextHost.DataContextChanged-=walking);contextHost.DataContext=other;
                    Require(walked&&!oldCurrent(),"Actual DataContext change synchronously revokes original callback before any return");RichHostRetired(retired,[pixel]);await Idle();RichHostStructureRetired(retired);
                    Require(ReferenceEquals(mount.Content,replacement),"Deferred exact-old-mount cleanup preserves borrowed replacement while mismatched context prevents fresh editing projection");
                    contextHost.DataContext=note;Require(!oldCurrent(),"DataContext away-back cannot renew original composite authority");await Idle();var fresh=RichHost(isSticky?(Window)sticky:main);
                    Require(!ReferenceEquals(fresh.Owner,retired.Owner)&&!fresh.Editor.RichInput.IsReadOnly&&note.Document.SourceJson==original&&note.EditVersion==originalVersion&&note.ContentVersion==originalContentVersion&&session.AttachmentPreviewEpoch==originalEpoch&&pixel.Source is null,"Returning exact DataContext creates fresh checked editor without source/version/epoch mutation or pixel resurrection");
                }
                else if(scenario is "child-content-awayback" or "child-editor-awayback")
                {
                    var owner=(UserControl)mainHost.Owner;var graphRoot=Field<DockPanel>(owner,"root");var oldCurrent=Field<Func<bool>>(mainHost.Editor,"current");bool walked=false;
                    DependencyPropertyChangedEventHandler walking=(_,e)=>
                    {
                        if(walked||e.NewValue is not false)return;walked=true;Require(RichNativeWalkBusy(),"Actual child visibility callback retains native-walk barrier through WPF enumeration");RichHostRetired(mainHost,[mainPixels]);int count=graphRoot.Children.Count;
                        PumpRichHostNotifications();Require(RichNativeWalkBusy()&&graphRoot.Children.Count==count&&ReferenceEquals(Control<ContentControl>(main,"StructuredHost").Content,owner),"Nested real visibility walk cannot detach cached panel children or capture a fresh editor before true unwind");
                    };
                    mainHost.Editor.IsVisibleChanged+=walking;detach.Add(()=>mainHost.Editor.IsVisibleChanged-=walking);
                    if(scenario=="child-content-awayback"){owner.Content=new TextBlock{Text="synthetic replacement"};owner.Content=graphRoot;}
                    else{graphRoot.Children.Remove(mainHost.Editor);if(!graphRoot.Children.Contains(mainHost.Editor))graphRoot.Children.Add(mainHost.Editor);}
                    Require(walked&&!oldCurrent(),"Retired exact host callback cannot regain authority after native away-back");RichHostRetired(mainHost,[mainPixels]);Require(note.Document.SourceJson==original&&note.EditVersion==originalVersion,"Native composite Content/child away-back cannot renew original editing authority");await Idle();RichHostStructureRetired(mainHost);Require(!ReferenceEquals(RichHost(main).Owner,mainHost.Owner)&&!RichHost(main).Editor.RichInput.IsReadOnly,"Real native unwind permits only a fresh checked owner");
                }
                else if(scenario.StartsWith("setter",StringComparison.Ordinal))
                {
                    bool fired=false;object? interrupted=null;var host=Control<ContentControl>(main,"StructuredHost");var descriptor=DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty,typeof(ContentControl));EventHandler listener=(_,_)=>{if(!fired&&host.Content?.GetType().Name=="RichImageTextHost"){fired=true;interrupted=host.Content;Require(RichHostProperty<StructuredNoteEditor?>(interrupted!,"Editor") is null&&RichHostProperty<RichImageDocumentView?>(interrupted!,"ImageView") is null,"Exact empty composite is mounted before any child/context capture");if(scenario=="setter-source")note.Title="native bootstrap source reentry";else if(scenario=="setter-conceal")main.Hide();else{list.SelectedItem=other;list.SelectedItem=note;}}};descriptor.AddValueChanged(host,listener);detach.Add(()=>descriptor.RemoveValueChanged(host,listener));note.Title="setter source refresh";await Idle();Require(fired&&interrupted is not null,"Fresh composite publication reaches actual native Content setter observer");Require(RichHostProperty<bool>(interrupted!,"IsDisposed")&&RichHostProperty<StructuredNoteEditor?>(interrupted!,"Editor") is null&&RichHostProperty<RichImageDocumentView?>(interrupted!,"ImageView") is null,"Interrupted bootstrap cannot capture or mount any child after native selection reentry");Require(note.Document.SourceJson==original,"Native publish reentry cannot restore interrupted owner or apply unrelated text");if(scenario=="setter-conceal")Require(host.Content is null,"Concealed interrupted bootstrap remains empty");else Require(!ReferenceEquals(RichHost(main).Owner,interrupted)&&!RichHost(main).Editor.RichInput.IsReadOnly,"After failed bootstrap cleanup actual Idle creates a fresh writable owner for the current valid association");
                }
                else if(scenario=="generic-native-refresh")
                {
                    extraView=new RichImageDocumentView(session,note,()=>true,_=>{});var boundary=new RichImageNativeDockPanel();boundary.Children.Add(extraView);extraWindow=new Window{Content=boundary,Width=600,Height=500};extraWindow.Show();await Idle();
                    var previousText=Field<List<TextBlock>>(extraView,"ownedText").ToArray();int previousCount=extraView.BlocksHost.Children.Count;bool walked=false;
                    DependencyPropertyChangedEventHandler walking=(_,_)=>
                    {
                        walked=true;Require(RichNativeWalkBusy(),"Generic projection refresh fixture uses a real inherited native property walk");var table=mainHost.Editor.RichInput.Document.Blocks.OfType<Table>().Single();var cell=(Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock;mainHost.Editor.RichInput.Selection.Select(cell.ContentStart,cell.ContentEnd);mainHost.Editor.RichInput.Selection.Text="generic replacement";Require(note.EditVersion==originalVersion+1&&note.Text.Contains("generic replacement",StringComparison.Ordinal)&&ReferenceEquals(RichHost(main).Editor,mainHost.Editor)&&!mainHost.Editor.IsDisposed,"First actual closed editor mutation settles its own receipt while outer native walk remains active");var work=Field<object?>(extraView,"nativeRefreshWork");Require(work is not null&&previousText.All(text=>text.Text.Length==0),"Native-walk refresh redacts all exact prior full-view text before deferred structural rebuild");
                        mainHost.Editor.RichInput.Selection.Select(cell.ContentStart,cell.ContentEnd);mainHost.Editor.RichInput.Selection.Text="generic replacement final";Require(note.EditVersion==originalVersion+2&&note.Text.Contains("generic replacement final",StringComparison.Ordinal)&&ReferenceEquals(RichHost(main).Editor,mainHost.Editor),"Second real closed editor receipt retains exact continuation without fresh capture inside native walk");Require(ReferenceEquals(work,Field<object?>(extraView,"nativeRefreshWork")),"Repeated native-walk refresh coalesces into one weak work record");PumpRichHostNotifications();Require(RichNativeWalkBusy()&&extraView.BlocksHost.Children.Count==previousCount&&previousText.All(text=>text.Text.Length==0),"Nested pump cannot alter full-view panel children before actual native unwind");
                    };
                    boundary.DataContextChanged+=walking;detach.Add(()=>boundary.DataContextChanged-=walking);boundary.DataContext=new object();Require(walked,"Generic refresh traversed actual native callback");await Idle();
                    Require(extraView.BlocksHost.Children.Count==previousCount&&Field<List<TextBlock>>(extraView,"ownedText").Any(text=>text.Text.Contains("generic replacement final",StringComparison.Ordinal))&&extraView.ImageForBlock(block).Source is null&&ImageRawNodes(note.Document!).SequenceEqual(rawImages),"After native unwind generic full viewer rebuilds final exact current source without implicit decode or image-node loss");
                }
                else
                {
                    Require(await session.LockAsync()&&session.KeysReleased&&!session.IsBusy,"Actual host session lock reaches true settlement");RichHostRetired(mainHost,[mainPixels]);RichHostRetired(stickyHost,[stickyPixels]);await Idle();RichHostStructureRetired(mainHost);RichHostStructureRetired(stickyHost);Require(Control<ContentControl>(main,"StructuredHost").Content is null&&Control<ContentControl>(sticky,"StructuredHost").Content is null,"Lock clears actual Main/Sticky composite attachment");
                }
            }
            Require(cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Projection/typing/refresh/refusal makes no implicit plaintext/image save or ciphertext rewrite");
        }
        catch(Exception error){primary=error;throw;}
        finally
        {
            var failures=new List<Exception>();try{paused?.Release.Set();}catch(Exception error){failures.Add(error);}foreach(var task in pending)try{await task;}catch(Exception error){failures.Add(error);}
            foreach(var action in detach)try{action();}catch(Exception error){failures.Add(error);}
            try{extraView?.Dispose();}catch(Exception error){failures.Add(error);}try{extraWindow?.Close();}catch(Exception error){failures.Add(error);}
            bool cleaned=session is null;
            try
            {
                if(session is not null){try{await session.LockAsync();await session.WhenAttachmentReadsIdle;}catch(Exception error){failures.Add(error);}if(session.KeysReleased&&!session.IsBusy){try{if(main is not null){Invoke(main,"ReleaseSettledSession");session=null;}else{session.Dispose();session=null;}cleaned=true;}catch(Exception error){failures.Add(error);}}}
                if(main is not null){try{SetField(main,"confirmedExit",true);main.Close();}catch(Exception error){failures.Add(error);}}
                try{paused?.Dispose();}catch(Exception error){failures.Add(error);}
            }
            finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);CryptographicOperations.ZeroMemory(secret);if(cleaned&&Directory.Exists(root))try{Directory.Delete(root,true);}catch(Exception error){failures.Add(error);}}
            if(failures.Count>0){if(primary is not null)failures.Insert(0,primary);throw new AggregateException("Synthetic v2 composite host cleanup failed",failures);}
        }
    }
}

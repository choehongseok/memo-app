using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;
namespace MemoApp.Windows;

// Read-only canonical v2 projection. No editable native document, URI resources,
// embedded native editor content, automatic decode, or image-related Undo.
public sealed class RichImageDocumentView : UserControl, IDisposable, IImageDisplayHost
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnPropertyChanged(args);}
    protected override void OnVisualParentChanged(DependencyObject oldParent){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualParentChanged(oldParent);}

    private SaveCoordinator? session;
    private EditingWorkspace? workspace;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private readonly ImagePreviewBackend backend;
    private CancellationTokenSource? cancellation;
    private long generation, observedEpoch;
    private bool refreshing, refreshPending;
    private readonly bool imageActionsOnly;
    private long uiGeneration;
    private readonly Dictionary<int,RenderedImageAction> renderedActions=[];
    private readonly List<TextBlock> ownedText=[];
    private ScrollViewer? projectionRoot;
    private RichImageTextPhase.NativeRequest? nativeRefreshWork;
    private sealed class RenderedImageAction(NoteDraft note,StyledDocument document,long version,long epoch,long uiGeneration,int blockIndex,Guid id,string alt,Image image,StackPanel panel,Button display,Button remove)
    {
        internal NoteDraft? Note=note;internal StyledDocument? Document=document;internal string? Alt=alt;
        internal readonly long Version=version,Epoch=epoch,UiGeneration=uiGeneration;internal readonly int BlockIndex=blockIndex;internal readonly Guid Id=id;
        internal readonly Image Image=image;internal readonly StackPanel Panel=panel;internal readonly Button Display=display,Remove=remove;
        internal RoutedEventHandler? DisplayHandler,RemoveHandler;private bool retired;
        internal void Revoke()
        {
            if(retired)return;retired=true;Note=null;Document=null;Alt=null;var displayHandler=DisplayHandler;var removeHandler=RemoveHandler;DisplayHandler=RemoveHandler=null;
            try{if(displayHandler is not null)Display.Click-=displayHandler;}catch{}try{if(removeHandler is not null)Remove.Click-=removeHandler;}catch{}
            foreach(var label in Panel.Children.OfType<TextBlock>().ToArray())try{label.Text="";}catch{}try{Image.Source=null;}catch{}try{Image.Visibility=Visibility.Collapsed;}catch{}RichImageTextPhase.CleanupNative(Panel.Dispatcher,Panel,UiGeneration,static (panel,_)=>{try{panel.Children.Clear();}catch{}});
        }
    }
    private static void RevokeRenderedActions(IEnumerable<RenderedImageAction> actions){foreach(var action in actions.ToArray())action.Revoke();}
    private void WireRenderedAction(RenderedImageAction action)
    {
        var weak=new WeakReference<RichImageDocumentView>(this);
        action.DisplayHandler=async(_,_)=>{if(weak.TryGetTarget(out var view)&&view.RenderedCurrent(action))await view.DisplayImageCoreAsync(action.BlockIndex,action);};
        action.RemoveHandler=(_,_)=>{if(weak.TryGetTarget(out var view)&&view.RenderedCurrent(action))view.RemoveImageCore(action.BlockIndex,action);};
        action.Display.Click+=action.DisplayHandler;action.Remove.Click+=action.RemoveHandler;
    }
    private StyledDocument? projected;
    private readonly Dictionary<int,Image> images=[];
    internal StackPanel BlocksHost {get;}=new RichImageNativeStackPanel();
    public bool IsDisposed {get;private set;}
    public RichImageDocumentView(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice)
        :this(session,note,current,notice,new ImagePreviewBackend()){}
    internal RichImageDocumentView(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice,ImagePreviewBackend backend,bool imageActionsOnly=false)
    {
        Dispatcher.VerifyAccess();this.session=session;workspace=session.Workspace;this.note=note;this.current=current;this.notice=notice;this.backend=backend;this.imageActionsOnly=imageActionsOnly;observedEpoch=session.AttachmentPreviewEpoch;
        Content=projectionRoot=new ScrollViewer{Content=BlocksHost,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=600};
        session.Conceal+=Dispose;session.Changed+=StateChanged;note.PropertyChanged+=NoteChanged;
        workspace.AttachmentReadInvalidating+=AttachmentReadsInvalidating;
        IsVisibleChanged+=VisibilityChanged;Dispatcher.ShutdownStarted+=DispatcherClosing;CompositionTarget.Rendering+=Rendering;Refresh();
    }
    internal Image ImageForBlock(int index)=>images[index];
    private bool Live()=>!IsDisposed&&session is {IsLocked:false} active&&note is {IsClosed:false,IsDeleted:false,Mode:"rich"} source&&active.Workspace.Notes.Contains(source)&&current?.Invoke()==true&&!IsDisposed&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&source is {IsClosed:false,IsDeleted:false,Mode:"rich"};
    private void Report(string message){if(Live())notice?.Invoke(message);}
    private void NoteChanged(object? sender,PropertyChangedEventArgs e)
    {
        InvalidateImageDisplay();if(!Live()){Dispose();return;}
        if(!ReferenceEquals(projected,note!.Document))Refresh();
    }
    private void StateChanged()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;if(!Live()){Dispose();return;}
        if(session!.AttachmentPreviewEpoch!=observedEpoch){observedEpoch=session.AttachmentPreviewEpoch;InvalidateImageDisplay();}
    }
    private void AttachmentReadsInvalidating(NoteDraft? source)=>InvalidateImageDisplay();
    private void Rendering(object? sender,EventArgs e)=>StateChanged();
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){if(!IsVisible)InvalidateImageDisplay();}
    private void DispatcherClosing(object? sender,EventArgs e)=>Dispose();
    public void InvalidateImageDisplay()
    {
        Dispatcher.VerifyAccess();generation++;ImagePreviewAdmission.Forget(this);var cancel=cancellation;cancellation=null;
        try{cancel?.Cancel();}catch{}try{cancel?.Dispose();}catch{}
        foreach(var image in images.Values.ToArray()){try{image.Visibility=Visibility.Collapsed;}catch{}try{image.Source=null;}catch{}}
    }
    private void RedactOwnedText()
    {
        var previous=ownedText.ToArray();ownedText.Clear();foreach(var text in previous){try{text.Text="";}catch{}try{text.ToolTip=null;}catch{}}
    }
    private void TrackText(FrameworkElement element)
    {
        if(element is TextBlock text)ownedText.Add(text);
        else if(element is Panel panel)foreach(var child in panel.Children.OfType<FrameworkElement>())TrackText(child);
        else if(element is Border{Child:FrameworkElement child})TrackText(child);
    }
    private void DeferNativeRefresh()
    {
        RevokeRenderedActions(renderedActions.Values);renderedActions.Clear();RedactOwnedText();projected=null;InvalidateImageDisplay();long request=++uiGeneration;
        if(nativeRefreshWork is not null){nativeRefreshWork.Update(request);return;}
        nativeRefreshWork=RichImageTextPhase.CleanupNative(Dispatcher,this,request,static (view,generation)=>{view.nativeRefreshWork=null;if(!view.IsDisposed&&view.uiGeneration==generation)view.Refresh();});
    }
    private void Refresh()
    {
        if(!Live()){Dispose();return;}if(RichImageTextPhase.NativeWalkBusy(Dispatcher)){DeferNativeRefresh();return;}using var phase=RichImageTextPhase.Enter(Dispatcher);if(refreshing){refreshPending=true;return;}refreshing=true;var nextActions=new Dictionary<int,RenderedImageAction>();bool published=false;RevokeRenderedActions(renderedActions.Values);renderedActions.Clear();RedactOwnedText();
        try
        {
            InvalidateImageDisplay();long projection=++uiGeneration;var source=note!;var document=source.Document!;long version=source.EditVersion,epoch=session!.AttachmentPreviewEpoch,request=generation;
            bool Same()=>Live()&&uiGeneration==projection&&ReferenceEquals(note,source)&&ReferenceEquals(source.Document,document)&&source.EditVersion==version&&generation==request;
            var nodes=new List<FrameworkElement>();var nextImages=new Dictionary<int,Image>();
            var info=RichDocumentCodec.Inspect(document);
            if(document.SchemaVersion!=2||!info.Supported){if(!imageActionsOnly){var element=new TextBlock{Text=source.Text,TextWrapping=TextWrapping.Wrap};nodes.Add(element);TrackText(element);}}
            else
            {
                using var json=JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});int index=0;
                foreach(var block in json.RootElement.GetProperty("nodes").EnumerateArray())
                {
                    if(block.GetProperty("type").GetString()=="image")
                    {
                        int blockIndex=index;var image=new Image{MaxWidth=1024,MaxHeight=360,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};nextImages.Add(index,image);
                        var panel=new RichImageNativeStackPanel{Margin=new(0,4,0,8)};panel.Children.Add(new TextBlock{Text="[이미지: "+block.GetProperty("alt").GetString()+"]",TextWrapping=TextWrapping.Wrap});
                        var actions=new WrapPanel();var display=new Button{Content="이미지 표시",Padding=new(6,3,6,3),Margin=new(0,0,6,0)};var remove=new Button{Content="이미지 블록 제거",Padding=new(6,3,6,3)};
                        var action=new RenderedImageAction(source,document,version,epoch,projection,blockIndex,Guid.Parse(block.GetProperty("attachmentId").GetString()!),block.GetProperty("alt").GetString()!,image,panel,display,remove);nextActions.Add(blockIndex,action);
                        WireRenderedAction(action);actions.Children.Add(display);actions.Children.Add(remove);panel.Children.Add(actions);panel.Children.Add(image);nodes.Add(panel);TrackText(panel);
                    }
                    else if(!imageActionsOnly){var element=RenderBlock(block);nodes.Add(element);TrackText(element);}
                    index++;
                }
            }
            if(!Same()){refreshPending=Live();return;}
            projected=document;BlocksHost.Children.Clear();
            if(!Same()){refreshPending=Live();return;}
            images.Clear();renderedActions.Clear();foreach(var pair in nextImages)images.Add(pair.Key,pair.Value);foreach(var pair in nextActions)renderedActions.Add(pair.Key,pair.Value);
            foreach(var element in nodes){if(!Same()){InvalidateImageDisplay();refreshPending=Live();return;}BlocksHost.Children.Add(element);if(!Same()){InvalidateImageDisplay();refreshPending=Live();return;}}published=true;
        }
        catch{if(!IsDisposed){InvalidateImageDisplay();try{BlocksHost.Children.Clear();}catch{}images.Clear();renderedActions.Clear();projected=null;}}
        finally
        {
            if(!published){RevokeRenderedActions(nextActions.Values);RedactOwnedText();}refreshing=false;if(refreshPending&&!IsDisposed){refreshPending=false;long request=uiGeneration;Dispatcher.BeginInvoke(RichImageTextPhase.WeakWork(this,request,0,static (view,g,_)=>{if(!view.IsDisposed&&view.uiGeneration==g)view.Refresh();}),DispatcherPriority.Background);}
        }
    }
    private static TextBlock RenderRuns(JsonElement owner,string prefix="")
    {
        var text=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,4)};if(prefix.Length!=0)text.Inlines.Add(new Run(prefix));
        foreach(var entry in owner.GetProperty("runs").EnumerateArray())
        {
            var run=new Run(entry.GetProperty("text").GetString()!);
            if(entry.TryGetProperty("bold",out var bold)&&bold.GetBoolean())run.FontWeight=FontWeights.Bold;
            var decoration=new TextDecorationCollection();if(entry.TryGetProperty("underline",out var underline)&&underline.GetBoolean())decoration.Add(TextDecorations.Underline[0]);if(entry.TryGetProperty("strike",out var strike)&&strike.GetBoolean())decoration.Add(TextDecorations.Strikethrough[0]);run.TextDecorations=decoration;
            if(entry.TryGetProperty("fontSize",out var size))run.FontSize=size.GetDouble();
            if(entry.TryGetProperty("fontFamily",out var font))run.FontFamily=new FontFamily(font.GetString()!);
            if(entry.TryGetProperty("foreground",out var foreground))run.Foreground=ColorBrush(foreground.GetString()!);
            if(entry.TryGetProperty("background",out var background))run.Background=ColorBrush(background.GetString()!);
            // Link text retains its known style and label; this read-only viewer never opens it.
            if(entry.TryGetProperty("link",out var link))run.ToolTip=link.GetString();text.Inlines.Add(run);
        }
        return text;
    }
    private static FrameworkElement RenderBlock(JsonElement block)
    {
        switch(block.GetProperty("type").GetString())
        {
            case "paragraph":return RenderRuns(block);
            case "list":case "checklist":
                var list=new StackPanel{Margin=new(8,0,0,4)};bool checklist=block.GetProperty("type").GetString()=="checklist",ordered=!checklist&&block.GetProperty("ordered").GetBoolean();int itemIndex=1;
                foreach(var item in block.GetProperty("items").EnumerateArray())list.Children.Add(RenderRuns(item,checklist?(item.GetProperty("checked").GetBoolean()?"[x] ":"[ ] "):ordered?$"{itemIndex++}. ":"• "));return list;
            case "table":
                var grid=new Grid{Margin=new(0,0,0,6)};int rowIndex=0;
                foreach(var row in block.GetProperty("rows").EnumerateArray())
                {
                    grid.RowDefinitions.Add(new());int columnIndex=0;
                    foreach(var cell in row.EnumerateArray())
                    {
                        if(rowIndex==0)grid.ColumnDefinitions.Add(new());var border=new Border{BorderBrush=Brushes.Gray,BorderThickness=new(1),Padding=new(4),Child=RenderRuns(cell)};Grid.SetRow(border,rowIndex);Grid.SetColumn(border,columnIndex++);grid.Children.Add(border);
                    }
                    rowIndex++;
                }
                return grid;
            default:throw new InvalidOperationException("Unsupported canonical block");
        }
    }
    private static SolidColorBrush ColorBrush(string hex)
    {
        byte Part(int offset)=>byte.Parse(hex.AsSpan(offset,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return new(hex.Length==9?Color.FromArgb(Part(1),Part(3),Part(5),Part(7)):Color.FromRgb(Part(1),Part(3),Part(5)));
    }
    private bool RenderedPure(RenderedImageAction action)=>!IsDisposed&&action.Note is not null&&action.Document is not null&&uiGeneration==action.UiGeneration&&ReferenceEquals(note,action.Note)&&ReferenceEquals(action.Note.Document,action.Document)&&action.Note.EditVersion==action.Version&&session?.AttachmentPreviewEpoch==action.Epoch&&renderedActions.TryGetValue(action.BlockIndex,out var original)&&ReferenceEquals(original,action)&&images.TryGetValue(action.BlockIndex,out var image)&&ReferenceEquals(image,action.Image)&&ReferenceEquals(action.Panel.Parent,BlocksHost)&&RichDocumentCodec.Images(action.Document).Any(image=>image.BlockIndex==action.BlockIndex&&image.AttachmentId==action.Id&&image.Alt==action.Alt)&&action.Panel.Children.OfType<WrapPanel>().Any(panel=>panel.Children.Contains(action.Display)&&panel.Children.Contains(action.Remove));
    private bool RenderedCurrent(RenderedImageAction action)=>RenderedPure(action)&&Live()&&RenderedPure(action);
    public Task<bool> DisplayImageAsync(int blockIndex)=>DisplayImageCoreAsync(blockIndex,null);
    private Task<bool> DisplayImageCoreAsync(int blockIndex,RenderedImageAction? action)
    {
        Dispatcher.VerifyAccess();if(!Live()||!IsVisible||!images.ContainsKey(blockIndex)||action is not null&&!RenderedPure(action))return Task.FromResult(false);
        if(!ImagePreviewAdmission.TryEnter(Dispatcher)){Report("다른 이미지/OCR 처리 중입니다. 처리 후 다시 이미지 표시를 눌러 주세요.");return Task.FromResult(false);}
        AttachmentReadLease? lease=null;CancellationTokenSource? cancel=null;
        try
        {
            ImagePreviewAdmission.ClearDisplayed();InvalidateImageDisplay();var active=session!;var source=note!;var document=source.Document!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,request=generation;observedEpoch=epoch;
            var reference=RichDocumentCodec.Images(document).Single(item=>item.BlockIndex==blockIndex);var image=images[blockIndex];cancel=new();cancellation=cancel;var token=cancel.Token;
            bool Allowed()=>Live()&&(action is null||RenderedPure(action))&&IsVisible&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&ReferenceEquals(source.Document,document)&&source.EditVersion==version&&epoch==active.AttachmentPreviewEpoch&&request==generation&&!token.IsCancellationRequested&&images.TryGetValue(blockIndex,out var target)&&ReferenceEquals(target,image)&&RichDocumentCodec.Images(source.Document!).Any(item=>item.BlockIndex==blockIndex&&item.AttachmentId==reference.AttachmentId)&&active.IsAttachmentPreviewCurrent(source,reference.AttachmentId,version,epoch);
            if(!Allowed())throw new OperationCanceledException();lease=active.CreateAttachmentReadLease(source,reference.AttachmentId,version);if(!Allowed())throw new OperationCanceledException();
            return RunDisplayAsync(backend,lease,cancel,Dispatcher,Allowed,image);
        }
        catch{lease?.Dispose();cancel?.Dispose();if(ReferenceEquals(cancellation,cancel))cancellation=null;ImagePreviewAdmission.Exit();return Task.FromResult(false);}
    }
    private static Task<OwnedBgraRaster> DecodeAsync(ImagePreviewBackend backend,AttachmentReadLease lease,CancellationToken token)=>Task.Run(()=>backend.Decode(lease,token));
    private async Task<bool> RunDisplayAsync(ImagePreviewBackend decoder,AttachmentReadLease lease,CancellationTokenSource cancel,Dispatcher context,Func<bool> allowed,Image image)
    {
        OwnedBgraRaster? raster=null;var token=cancel.Token;
        try
        {
            raster=await DecodeAsync(decoder,lease,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();var detached=raster;
            return await context.InvokeAsync(()=>
            {
                if(!allowed())return false;var bitmap=decoder.Create(detached);if(!allowed())return false;
                ImagePreviewAdmission.PublishHost(this);
                image.Source=bitmap;if(!allowed()){InvalidateImageDisplay();return false;}
                image.Visibility=Visibility.Visible;if(!allowed()){InvalidateImageDisplay();return false;}return true;
            },DispatcherPriority.Background,token).Task.ConfigureAwait(false);
        }
        catch
        {
            if(!context.HasShutdownStarted)try{await context.InvokeAsync(()=>{InvalidateImageDisplay();Report("이미지를 표시하지 못했습니다. 제한 PNG 형식·크기와 저장 상태를 확인하세요. 원본은 보존했습니다.");},DispatcherPriority.Background).Task.ConfigureAwait(false);}catch{}
            return false;
        }
        finally{raster?.Dispose();lease.Dispose();cancel.Dispose();ImagePreviewAdmission.Exit();}
    }
    public bool RemoveImage(int blockIndex)=>RemoveImageCore(blockIndex,null);
    private bool RemoveImageCore(int blockIndex,RenderedImageAction? action)
    {
        Dispatcher.VerifyAccess();if(!Live()||!IsVisible||action is not null&&!RenderedPure(action))return false;var active=session!;var source=note!;long version=source.EditVersion;
        try{active.RemoveInlineImage(source,blockIndex,version);Report("이미지 블록을 제거했습니다. 첨부 원본과 이전 이력은 보존하며 자동 저장 상태를 확인하세요.");return true;}
        catch{Report("이미지 블록 제거를 적용하지 못했습니다. 현재 문서와 저장 상태를 확인하세요.");return false;}
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;using var phase=RichImageTextPhase.Enter(Dispatcher);IsDisposed=true;long retiredGeneration=++uiGeneration;var pendingRefresh=nativeRefreshWork;nativeRefreshWork=null;pendingRefresh?.Dispose();
        var active=session;var source=note;var previousWorkspace=workspace;session=null;workspace=null;note=null;current=null;notice=null;projected=null;refreshPending=false;
        foreach(Action cleanup in new Action[]{()=>{if(previousWorkspace is not null)previousWorkspace.AttachmentReadInvalidating-=AttachmentReadsInvalidating;},()=>{if(active is not null)active.Conceal-=Dispose;},()=>{if(active is not null)active.Changed-=StateChanged;},()=>{if(source is not null)source.PropertyChanged-=NoteChanged;},()=>IsVisibleChanged-=VisibilityChanged,()=>Dispatcher.ShutdownStarted-=DispatcherClosing,()=>CompositionTarget.Rendering-=Rendering,()=>InvalidateImageDisplay(),()=>RevokeRenderedActions(renderedActions.Values),RedactOwnedText,()=>Visibility=Visibility.Collapsed})try{cleanup();}catch{}
        renderedActions.Clear();images.Clear();
        RichImageTextPhase.CleanupNative(Dispatcher,this,retiredGeneration,static (view,generation)=>
        {
            if(!view.IsDisposed||view.uiGeneration!=generation)return;try{if(ReferenceEquals(view.Content,view.projectionRoot))view.Content=null;}catch{}try{view.BlocksHost.Children.Clear();}catch{}view.projectionRoot=null;
        });
    }
}

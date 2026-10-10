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
    private SaveCoordinator? session;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private readonly ImagePreviewBackend backend;
    private CancellationTokenSource? cancellation;
    private long generation, observedEpoch;
    private bool refreshing, refreshPending;
    private StyledDocument? projected;
    private readonly Dictionary<int,Image> images=[];
    internal StackPanel BlocksHost {get;}=new();
    public bool IsDisposed {get;private set;}
    public RichImageDocumentView(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice)
        :this(session,note,current,notice,new ImagePreviewBackend()){}
    internal RichImageDocumentView(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice,ImagePreviewBackend backend)
    {
        Dispatcher.VerifyAccess();this.session=session;this.note=note;this.current=current;this.notice=notice;this.backend=backend;observedEpoch=session.AttachmentPreviewEpoch;
        Content=new ScrollViewer{Content=BlocksHost,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=600};
        session.Conceal+=Dispose;session.Changed+=StateChanged;note.PropertyChanged+=NoteChanged;
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
    private void Rendering(object? sender,EventArgs e)=>StateChanged();
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){if(!IsVisible)InvalidateImageDisplay();}
    private void DispatcherClosing(object? sender,EventArgs e)=>Dispose();
    public void InvalidateImageDisplay()
    {
        Dispatcher.VerifyAccess();generation++;ImagePreviewAdmission.Forget(this);var cancel=cancellation;cancellation=null;
        try{cancel?.Cancel();}catch{}try{cancel?.Dispose();}catch{}
        foreach(var image in images.Values.ToArray()){try{image.Visibility=Visibility.Collapsed;}catch{}try{image.Source=null;}catch{}}
    }
    private void Refresh()
    {
        if(!Live()){Dispose();return;}if(refreshing){refreshPending=true;return;}refreshing=true;
        try
        {
            InvalidateImageDisplay();var source=note!;var document=source.Document!;long version=source.EditVersion,request=generation;
            bool Same()=>Live()&&ReferenceEquals(note,source)&&ReferenceEquals(source.Document,document)&&source.EditVersion==version&&generation==request;
            var nodes=new List<FrameworkElement>();var nextImages=new Dictionary<int,Image>();
            var info=RichDocumentCodec.Inspect(document);
            if(document.SchemaVersion!=2||!info.Supported)nodes.Add(new TextBlock{Text=source.Text,TextWrapping=TextWrapping.Wrap});
            else
            {
                using var json=JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});int index=0;
                foreach(var block in json.RootElement.GetProperty("nodes").EnumerateArray())
                {
                    if(block.GetProperty("type").GetString()=="image")
                    {
                        int blockIndex=index;var image=new Image{MaxWidth=1024,MaxHeight=360,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};nextImages.Add(index,image);
                        var panel=new StackPanel{Margin=new(0,4,0,8)};panel.Children.Add(new TextBlock{Text="[이미지: "+block.GetProperty("alt").GetString()+"]",TextWrapping=TextWrapping.Wrap});
                        var actions=new WrapPanel();var display=new Button{Content="이미지 표시",Padding=new(6,3,6,3),Margin=new(0,0,6,0)};var remove=new Button{Content="이미지 블록 제거",Padding=new(6,3,6,3)};
                        display.Click+=async(_,_)=>await DisplayImageAsync(blockIndex);remove.Click+=(_,_)=>RemoveImage(blockIndex);actions.Children.Add(display);actions.Children.Add(remove);panel.Children.Add(actions);panel.Children.Add(image);nodes.Add(panel);
                    }
                    else nodes.Add(RenderBlock(block));
                    index++;
                }
            }
            if(!Same()){refreshPending=Live();return;}
            projected=document;BlocksHost.Children.Clear();
            if(!Same()){refreshPending=Live();return;}
            images.Clear();foreach(var pair in nextImages)images.Add(pair.Key,pair.Value);
            foreach(var element in nodes){if(!Same()){InvalidateImageDisplay();refreshPending=Live();return;}BlocksHost.Children.Add(element);if(!Same()){InvalidateImageDisplay();refreshPending=Live();return;}}
        }
        catch{if(!IsDisposed){InvalidateImageDisplay();try{BlocksHost.Children.Clear();}catch{}images.Clear();projected=null;}}
        finally
        {
            refreshing=false;if(refreshPending&&!IsDisposed){refreshPending=false;Dispatcher.BeginInvoke(new Action(Refresh),DispatcherPriority.Background);}
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
    public Task<bool> DisplayImageAsync(int blockIndex)
    {
        Dispatcher.VerifyAccess();if(!Live()||!IsVisible||!images.ContainsKey(blockIndex))return Task.FromResult(false);
        if(!ImagePreviewAdmission.TryEnter(Dispatcher)){Report("다른 이미지/OCR 처리 중입니다. 처리 후 다시 이미지 표시를 눌러 주세요.");return Task.FromResult(false);}
        AttachmentReadLease? lease=null;CancellationTokenSource? cancel=null;
        try
        {
            ImagePreviewAdmission.ClearDisplayed();InvalidateImageDisplay();var active=session!;var source=note!;var document=source.Document!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,request=generation;observedEpoch=epoch;
            var reference=RichDocumentCodec.Images(document).Single(item=>item.BlockIndex==blockIndex);var image=images[blockIndex];cancel=new();cancellation=cancel;var token=cancel.Token;
            bool Allowed()=>Live()&&IsVisible&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&ReferenceEquals(source.Document,document)&&source.EditVersion==version&&epoch==active.AttachmentPreviewEpoch&&request==generation&&!token.IsCancellationRequested&&images.TryGetValue(blockIndex,out var target)&&ReferenceEquals(target,image)&&RichDocumentCodec.Images(source.Document!).Any(item=>item.BlockIndex==blockIndex&&item.AttachmentId==reference.AttachmentId)&&active.IsAttachmentPreviewCurrent(source,reference.AttachmentId,version,epoch);
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
    public bool RemoveImage(int blockIndex)
    {
        Dispatcher.VerifyAccess();if(!Live()||!IsVisible)return false;var active=session!;var source=note!;long version=source.EditVersion;
        try{active.RemoveInlineImage(source,blockIndex,version);Report("이미지 블록을 제거했습니다. 첨부 원본과 이전 이력은 보존하며 자동 저장 상태를 확인하세요.");return true;}
        catch{Report("이미지 블록 제거를 적용하지 못했습니다. 현재 문서와 저장 상태를 확인하세요.");return false;}
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;IsDisposed=true;InvalidateImageDisplay();var active=session;var source=note;session=null;note=null;current=null;notice=null;projected=null;refreshPending=false;
        if(active is not null){active.Conceal-=Dispose;active.Changed-=StateChanged;}if(source is not null)source.PropertyChanged-=NoteChanged;IsVisibleChanged-=VisibilityChanged;Dispatcher.ShutdownStarted-=DispatcherClosing;CompositionTarget.Rendering-=Rendering;
        try{Visibility=Visibility.Collapsed;}catch{}try{Content=null;}catch{}try{BlocksHost.Children.Clear();}catch{}images.Clear();
    }
}

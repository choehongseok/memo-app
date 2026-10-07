using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
// Read-only projection: no HTML/XAML, media, hyperlinks, source writeback or external resources.
public sealed class MarkdownNotePreview:UserControl,IDisposable
{
    private NoteDraft? note;
    private Func<bool>? current;
    private MarkdownPreviewQueue? queue;
    private readonly object deliveryGate=new();
    private MarkdownPreviewUpdate? pendingResult;
    private bool posted,disposed;
    private string? requestedSource;
    private long requestedVersion,requestedGeneration;
    private readonly StackPanel content=new();
    private readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4)};
    private readonly DispatcherTimer debounce=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
    public Task WhenIdle=>queue?.WhenIdle??Task.CompletedTask;
    public bool IsDisposed=>disposed;
    public string? CleanupErrorCode{get;private set;}
    public MarkdownNotePreview(NoteDraft note,Func<bool> current,Func<string,MarkdownPreview>? parse=null)
    {
        Dispatcher.VerifyAccess();this.note=note;this.current=current;queue=new(Ready,parse);
        var root=new DockPanel();DockPanel.SetDock(state,Dock.Top);root.Children.Add(state);root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        debounce.Tick+=DebounceTick;note.PropertyChanged+=DraftChanged;RequestCurrent();
    }
    private bool Live()
    {var target=note;return !disposed&&target is {Mode:"markdown",IsClosed:false,IsDeleted:false}&&current?.Invoke()==true&&!disposed&&ReferenceEquals(note,target)&&target is {Mode:"markdown",IsClosed:false,IsDeleted:false};}
    private void DraftChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(disposed)return;if(!Live()){Dispose();return;}
        // Metadata versions are included in final authority; re-request without touching raw content.
        if(e.PropertyName is nameof(NoteDraft.EditVersion) or nameof(NoteDraft.Mode)){debounce.Stop();debounce.Start();}
    }
    private void DebounceTick(object? sender,EventArgs e){debounce.Stop();RequestCurrent();}
    private void RequestCurrent()
    {
        if(!Live()){Dispose();return;}var target=note!;string source=target.Text;long version=target.EditVersion;
        content.Children.Clear();state.Text="안전 미리보기 준비 중 · 원문은 그대로 저장합니다.";
        if(!Live()||!ReferenceEquals(note,target)||target.Text!=source||target.EditVersion!=version){if(Live()){debounce.Stop();debounce.Start();}return;}
        requestedSource=source;requestedVersion=version;requestedGeneration=queue!.Request(source);
        if(requestedGeneration==0)state.Text="미리보기 한도/Unicode 범위 초과 · 원문은 보존합니다.";
    }
    private void Ready(MarkdownPreviewUpdate update)
    {
        lock(deliveryGate)
        {
            if(disposed)return;pendingResult=update;if(posted)return;posted=true;
        }
        try{Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(PublishPending));}
        catch(InvalidOperationException){lock(deliveryGate){pendingResult=null;posted=false;}}
    }
    private void PublishPending()
    {
        MarkdownPreviewUpdate? update;lock(deliveryGate){update=pendingResult;pendingResult=null;posted=false;if(disposed)return;}
        if(update is null)return;if(!Live()){Dispose();return;}
        var target=note!;var owner=queue!;string? source=requestedSource;long version=requestedVersion,generation=requestedGeneration;
        bool Same()=>Live()&&ReferenceEquals(note,target)&&ReferenceEquals(queue,owner)&&target.Text==source&&target.EditVersion==version&&requestedGeneration==generation&&update.Generation==generation&&owner.IsCurrent(generation);
        void Reject(){if(!Live())Dispose();else{content.Children.Clear();debounce.Stop();debounce.Start();}}
        if(!Same()){Reject();return;}
        // Build detached inert text only, then check the complete authority directly before publication.
        var blocks=new List<TextBlock>();
        foreach(var block in update.Preview.Blocks)
        {
            var line=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(4,2,4,4)};
            if(block.Kind=="heading"){line.FontWeight=FontWeights.Bold;line.FontSize=block.Level switch{1=>28,2=>24,3=>20,_=>18};}
            if(block.Kind=="list-item")line.Inlines.Add(new Run("• "));
            foreach(var span in block.Spans){var run=new Run(span.Text);if(span.Strong)run.FontWeight=FontWeights.Bold;if(span.Italic)run.FontStyle=FontStyles.Italic;if(span.Code)run.FontFamily=new FontFamily("Consolas");line.Inlines.Add(run);}blocks.Add(line);
        }
        if(!Same()){Reject();return;}content.Children.Clear();
        foreach(var block in blocks){if(!Same()){Reject();return;}content.Children.Add(block);}
        if(Same())state.Text=update.Preview.Message;else Reject();
    }
    public void ApplyPreferences(MemoApp.Core.Storage.UiPreferences preferences)
    {if(disposed)return;Foreground=preferences.DarkMode?Brushes.White:Brushes.Black;Background=preferences.DarkMode?new SolidColorBrush(Color.FromRgb(42,47,57)):Brushes.White;state.Foreground=preferences.DarkMode?Brushes.Silver:Brushes.Gray;}
    public void Dispose()
    {
        Dispatcher.VerifyAccess();NoteDraft? oldNote;MarkdownPreviewQueue? oldQueue;
        lock(deliveryGate){if(disposed)return;disposed=true;pendingResult=null;posted=false;oldNote=note;oldQueue=queue;note=null;queue=null;current=null;requestedSource=null;requestedVersion=requestedGeneration=0;}
        if(oldNote is not null)oldNote.PropertyChanged-=DraftChanged;debounce.Stop();debounce.Tick-=DebounceTick;oldQueue?.Dispose();
        foreach(Action cleanup in new Action[]{()=>Visibility=Visibility.Collapsed,()=>content.Children.Clear(),()=>state.Text="",()=>DataContext=null})
        {try{cleanup();}catch(Exception error) when(error is not OutOfMemoryException){CleanupErrorCode="MARKDOWN_VIEW_CLEANUP_FAILURE";}}
    }
}

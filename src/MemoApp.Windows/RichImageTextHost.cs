using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;

namespace MemoApp.Windows;
// No note, document, workspace, lease or authority lives in the shared phase barrier.
internal static class RichImageTextPhase
{
    private sealed class State { internal int Depth,NativeDepth; internal event Action? Quiescent; internal void Signal(){foreach(Action callback in Quiescent?.GetInvocationList()??[])try{callback();}catch{}} internal void Add(Action action)=>Quiescent+=action;internal void Remove(Action action)=>Quiescent-=action; }
    private static readonly ConditionalWeakTable<Dispatcher,State> states=new();
    internal static bool Busy(Dispatcher dispatcher){dispatcher.VerifyAccess();return states.GetValue(dispatcher,static _=>new State()).Depth!=0;}
    internal static bool NativeWalkBusy(Dispatcher dispatcher){dispatcher.VerifyAccess();return states.GetValue(dispatcher,static _=>new State()).NativeDepth!=0;}
    internal static void Subscribe(Dispatcher dispatcher,Action action){dispatcher.VerifyAccess();states.GetValue(dispatcher,static _=>new State()).Add(action);}
    internal static void Unsubscribe(Dispatcher dispatcher,Action action){dispatcher.VerifyAccess();states.GetValue(dispatcher,static _=>new State()).Remove(action);}
    internal static IDisposable Enter(Dispatcher dispatcher){dispatcher.VerifyAccess();var state=states.GetValue(dispatcher,static _=>new State());var scope=new Scope(state);state.Depth++;return scope;}
    internal static Action WeakWake<T>(Dispatcher dispatcher,T owner,long generation,long epoch,Action<T,Action,long,long> callback) where T:class
        =>new Wake<T>(dispatcher,owner,generation,epoch,callback).Fire;
    private sealed class Wake<T>(Dispatcher dispatcher,T owner,long generation,long epoch,Action<T,Action,long,long> callback) where T:class
    {
        private readonly WeakReference<T> root=new(owner);
        internal void Fire(){Unsubscribe(dispatcher,Fire);if(root.TryGetTarget(out var live))callback(live,Fire,generation,epoch);}
    }
    internal static Action WeakWork<T>(T owner,long generation,long epoch,Action<T,long,long> callback) where T:class=>new Work<T>(owner,generation,epoch,callback).Run;
    private sealed class Work<T>(T owner,long generation,long epoch,Action<T,long,long> callback) where T:class
    {
        private readonly WeakReference<T> root=new(owner);
        internal void Run(){if(root.TryGetTarget(out var live))callback(live,generation,epoch);}
    }
    internal static IDisposable EnterNative(Dispatcher dispatcher){dispatcher.VerifyAccess();var state=states.GetValue(dispatcher,static _=>new State());var scope=new Scope(state,true);state.Depth++;state.NativeDepth++;return scope;}
    internal abstract class NativeRequest(Dispatcher dispatcher):IDisposable
    {
        private bool queued,waiting,canceled;
        internal void Post(){if(queued||canceled)return;queued=true;dispatcher.BeginInvoke(new Action(Run),DispatcherPriority.Background);}
        private void Wake(){Unsubscribe(dispatcher,Wake);waiting=false;Post();}
        private void Run(){queued=false;if(canceled)return;if(NativeWalkBusy(dispatcher)){if(!waiting){waiting=true;Subscribe(dispatcher,Wake);}return;}Execute();}
        internal void Start(){if(NativeWalkBusy(dispatcher))Post();else Execute();}
        internal abstract void Update(long generation);
        public void Dispose(){canceled=true;if(waiting){waiting=false;Unsubscribe(dispatcher,Wake);}}
        protected abstract void Execute();
    }
    private sealed class TargetWork<T>(Dispatcher dispatcher,T target,long generation,Action<T,long> cleanup):NativeRequest(dispatcher) where T:class
    {
        private readonly WeakReference<T> owner=new(target);private long requestedGeneration=generation;
        internal override void Update(long generation)=>requestedGeneration=generation;
        protected override void Execute(){if(owner.TryGetTarget(out var live))cleanup(live,requestedGeneration);}
    }
    private sealed class MountWork(Dispatcher dispatcher,ContentControl mount,object retired):NativeRequest(dispatcher)
    {
        private readonly WeakReference<ContentControl> outer=new(mount);private readonly WeakReference<object> old=new(retired);
        internal override void Update(long generation){}
        protected override void Execute(){if(outer.TryGetTarget(out var control)&&old.TryGetTarget(out var retired)&&ReferenceEquals(control.Content,retired)){control.Content=null;if(control.Content is null)control.Visibility=Visibility.Collapsed;}}
    }
    internal static NativeRequest CleanupNative<T>(Dispatcher dispatcher,T target,long generation,Action<T,long> cleanup) where T:class{var work=new TargetWork<T>(dispatcher,target,generation,cleanup);work.Start();return work;}
    internal static void RetireMount(Dispatcher dispatcher,ContentControl mount,object retired)=>new MountWork(dispatcher,mount,retired).Start();
    private sealed class Scope(State state,bool native=false):IDisposable
    {
        private State? owned=state;
        public void Dispose(){var current=owned;owned=null;if(current is not null){if(native)current.NativeDepth--;if(--current.Depth==0)current.Signal();}}
    }
}
public sealed class RichImageNativeDockPanel:DockPanel
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnPropertyChanged(args);}
    protected override void OnVisualParentChanged(DependencyObject oldParent){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualParentChanged(oldParent);}
    protected override void OnVisualChildrenChanged(DependencyObject added,DependencyObject removed){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualChildrenChanged(added,removed);}
}
public sealed class RichImageNativeContentControl:ContentControl
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnPropertyChanged(args);}
    protected override void OnVisualParentChanged(DependencyObject oldParent){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualParentChanged(oldParent);}
    protected override void OnVisualChildrenChanged(DependencyObject added,DependencyObject removed){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualChildrenChanged(added,removed);}
}
internal sealed class RichImageNativeStackPanel:StackPanel
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnPropertyChanged(args);}
    protected override void OnVisualParentChanged(DependencyObject oldParent){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualParentChanged(oldParent);}
    protected override void OnVisualChildrenChanged(DependencyObject added,DependencyObject removed){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualChildrenChanged(added,removed);}
}
internal sealed class RichImageTextHost:UserControl,IDisposable
{
    private SaveCoordinator? session;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private Action? refreshRequested;
    private readonly RichImageNativeDockPanel root=new();
    private long imageGeneration;
    private bool everVisible;
    private int projectionStage=-1;
    internal StructuredNoteEditor Editor{get;private set;}=null!;
    internal RichImageDocumentView ImageView{get;private set;}=null!;
    internal bool IsDisposed{get;private set;}
    internal RichImageTextHost(){Dispatcher.VerifyAccess();Content=root;}
    internal void Initialize(SaveCoordinator active,NoteDraft source,Func<bool> valid,Action<string> report,Action refresh)
    {
        Dispatcher.VerifyAccess();using var phase=RichImageTextPhase.Enter(Dispatcher);
        if(IsDisposed||session is not null)throw new InvalidOperationException("Composite already initialized or retired.");
        session=active;note=source;current=valid;notice=report;refreshRequested=refresh;projectionStage=0;
        try
        {
            if(!Current()){Dispose();return;}
            Editor=new(active,source,Current,Report);if(Editor.IsDisposed||IsDisposed){Dispose();return;}
            root.Children.Add(Editor);Editor.IsVisibleChanged+=ChildVisibilityChanged;if(!Current()){Dispose();return;}projectionStage=1;RefreshImageActions();
            active.Conceal+=Dispose;active.Workspace.AttachmentReadInvalidating+=Invalidating;source.PropertyChanged+=SourceChanged;IsVisibleChanged+=VisibilityChanged;
            if(!Current())Dispose();
        }
        catch{Dispose();throw;}
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnPropertyChanged(args);
        if(args.Property==ContentProperty&&session is not null&&!IsDisposed&&!ReferenceEquals(args.NewValue,root))RetireAndRefresh();
    }
    protected override void OnVisualParentChanged(DependencyObject oldParent){using var phase=RichImageTextPhase.EnterNative(Dispatcher);base.OnVisualParentChanged(oldParent);}
    private void RetireAndRefresh(){var refresh=refreshRequested;Dispose();refresh?.Invoke();}
    private void ChildVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if(IsDisposed||projectionStage!=2||!everVisible||args.NewValue is not false)return;
        if(ReferenceEquals(sender,ImageView)&&ImageView.IsDisposed)return;
        RetireAndRefresh();
    }
    private bool Current()
    {
        var active=session;var source=note;
        bool Children()=>ReferenceEquals(Content,root)&&(projectionStage==0?(Editor is null&&root.Children.Count==0||Editor is{IsDisposed:false}&&root.Children.Count==1&&root.Children.Contains(Editor)):Editor is{IsDisposed:false}&&root.Children.Contains(Editor)&&(projectionStage==1?(ImageView is null?root.Children.Count==1:root.Children.Count==2&&root.Children.Contains(ImageView)):projectionStage==2&&ImageView is not null&&root.Children.Count==2&&root.Children.Contains(ImageView)));
        bool Pure()=>!IsDisposed&&Children()&&active is{IsLocked:false}&&ReferenceEquals(active,session)&&source is{IsClosed:false,IsDeleted:false,Mode:"rich",Document:{SchemaVersion:2}}&&ReferenceEquals(source,note)&&active.Workspace.Notes.Contains(source);
        return Pure()&&current?.Invoke()==true&&Pure();
    }
    private void Report(string message){if(Current())notice?.Invoke(message);}
    private void SourceChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>RequestRefresh();
    private void Invalidating(NoteDraft? source)
    {
        if(IsDisposed)return;ImageView?.InvalidateImageDisplay();
        var refresh=refreshRequested;if(Editor.IsDisposed){Dispose();refresh?.Invoke();}else RequestRefresh();
    }
    private void RequestRefresh(){if(!IsDisposed)refreshRequested?.Invoke();}
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if(IsVisible)everVisible=true;else if(everVisible){var refresh=refreshRequested;Dispose();refresh?.Invoke();}
    }
    internal bool RetainCurrentEditor()
    {
        Dispatcher.VerifyAccess();return !RichImageTextPhase.Busy(Dispatcher)&&Current()&&Editor.HasCurrentRichImageTextProjection&&Current();
    }
    internal void RefreshImageActions()
    {
        Dispatcher.VerifyAccess();if(RichImageTextPhase.NativeWalkBusy(Dispatcher)){RequestRefresh();return;}using var phase=RichImageTextPhase.Enter(Dispatcher);if(IsDisposed)return;var active=session!;var source=note!;var document=source.Document;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;
        if(ImageView is{IsDisposed:false} previous&&ReferenceEquals(imageDocument,document)&&imageVersion==version&&imageEpoch==epoch)return;
        projectionStage=1;var old=ImageView;ImageView=null!;imageGeneration++;long generation=imageGeneration;try{if(old is not null)old.IsVisibleChanged-=ChildVisibilityChanged;}catch{}try{old?.Dispose();}catch{}try{if(old is not null)root.Children.Remove(old);}catch{}
        bool attached=false;RichImageDocumentView? created=null;
        bool Pure()=>!IsDisposed&&generation==imageGeneration&&ReferenceEquals(active,session)&&ReferenceEquals(source,note)&&ReferenceEquals(source.Document,document)&&source.EditVersion==version&&active.AttachmentPreviewEpoch==epoch&&(!attached||ReferenceEquals(ImageView,created)&&root.Children.Contains(created));
        bool Valid()=>Pure()&&Current()&&Pure();
        try
        {
            created=new(active,source,Valid,Report,new ImagePreviewBackend(),imageActionsOnly:true);
            if(!Valid()){created.Dispose();return;}ImageView=created;created.IsVisibleChanged+=ChildVisibilityChanged;DockPanel.SetDock(created,Dock.Bottom);root.Children.Insert(0,created);attached=true;if(!Valid()){Dispose();return;}imageDocument=document;imageVersion=version;imageEpoch=epoch;projectionStage=2;
        }
        catch{created?.Dispose();throw;}
    }
    // Source identity is tracked by this owner, not obtained from the child through reflection.
    private StyledDocument? imageDocument;
    private long imageVersion,imageEpoch;
    public void Dispose()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;using var phase=RichImageTextPhase.Enter(Dispatcher);IsDisposed=true;projectionStage=-1;imageGeneration++;var active=session;var source=note;var editor=Editor;var images=ImageView;
        session=null;note=null;current=null;notice=null;refreshRequested=null;imageDocument=null;Editor=null!;ImageView=null!;
        foreach(Action cleanup in new Action[]{()=>{if(active is not null)active.Conceal-=Dispose;},()=>{if(active is not null)active.Workspace.AttachmentReadInvalidating-=Invalidating;},()=>{if(source is not null)source.PropertyChanged-=SourceChanged;},()=>IsVisibleChanged-=VisibilityChanged,()=>{if(editor is not null)editor.IsVisibleChanged-=ChildVisibilityChanged;},()=>{if(images is not null)images.IsVisibleChanged-=ChildVisibilityChanged;},()=>editor?.Dispose(),()=>images?.Dispose(),()=>Visibility=Visibility.Collapsed})try{cleanup();}catch{}
        RichImageTextPhase.CleanupNative(Dispatcher,this,imageGeneration,static (owner,generation)=>{if(!owner.IsDisposed||owner.imageGeneration!=generation)return;try{owner.root.Children.Clear();}catch{}try{if(ReferenceEquals(owner.Content,owner.root))owner.Content=null;}catch{}});
    }
}

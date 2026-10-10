using System.Text.Json;
using System.Windows;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class StickyNoteWindow
{
    private SaveCoordinator? imageSession;
    private RichImageDocumentView? inlineImageView;
    private RichImageTextHost? richImageHost;
    private long richImageGeneration;
    private bool richImageRefreshQueued;
    private Action? richImageWake;
    private void RichImageDataContextChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if(richImageHost is not null)ClearInlineImageView();
        if(ReferenceEquals(args.NewValue,draft)&&ReferenceEquals(DataContext,draft)&&draft is{Mode:"rich",Document:{SchemaVersion:2}})RequestRichImageRefresh();
    }
    private void CancelRichImageRefresh()
    {
        var wake=richImageWake;richImageWake=null;richImageRefreshQueued=false;if(wake is not null)RichImageTextPhase.Unsubscribe(Dispatcher,wake);
    }
    private void RequestRichImageRefresh()
    {
        if(closed||!IsVisible||FoldToggle.IsChecked==true||imageSession is not{IsLocked:false}||!ReferenceEquals(DataContext,draft))return;long generation=richImageGeneration;
        if(RichImageTextPhase.Busy(Dispatcher))
        {
            if(richImageWake is not null)return;
            var wake=RichImageTextPhase.WeakWake(Dispatcher,this,generation,0,static (root,completed,g,_)=>{if(!Equals(root.richImageWake,completed))return;root.richImageWake=null;if(g==root.richImageGeneration&&!root.closed)root.RequestRichImageRefresh();});
            richImageWake=wake;RichImageTextPhase.Subscribe(Dispatcher,wake);return;
        }
        if(richImageRefreshQueued)return;richImageRefreshQueued=true;Dispatcher.BeginInvoke(RichImageTextPhase.WeakWork(this,generation,0,static (root,g,_)=>
        {if(g!=root.richImageGeneration)return;root.richImageRefreshQueued=false;if(root.closed||!root.IsVisible||root.FoldToggle.IsChecked==true||root.imageSession is not{IsLocked:false}||!ReferenceEquals(root.DataContext,root.draft))return;if(RichImageTextPhase.Busy(root.Dispatcher)){root.RequestRichImageRefresh();return;}root.ConfigureBody();}),System.Windows.Threading.DispatcherPriority.Background);
    }
    private void ClearInlineImageView()
    {
        using var phase=RichImageTextPhase.Enter(Dispatcher);CancelRichImageRefresh();richImageGeneration++;var owner=richImageHost;var previous=inlineImageView;richImageHost=null;inlineImageView=null;if(owner is not null)structuredEditor=null;
        foreach(Action cleanup in new Action[]{()=>owner?.Dispose(),()=>previous?.Dispose()})try{cleanup();}catch{}
        var retired=(object?)owner??previous;if(retired is not null)RichImageTextPhase.RetireMount(Dispatcher,StructuredHost,retired);

    }
    private void ConfigureInlineImageView()
    {
        if(!IsVisible||FoldToggle.IsChecked==true||!ReferenceEquals(DataContext,draft)){ClearInlineImageView();return;}
        if(imageSession is not{IsLocked:false} active){BodyEditor.Text=draft.Text;BodyEditor.Visibility=Visibility.Visible;return;}
        if(RichImageTextPhase.Busy(Dispatcher)){RequestRichImageRefresh();return;}
        if(richImageHost is{IsDisposed:false} retained&&ReferenceEquals(StructuredHost.Content,retained)&&retained.RetainCurrentEditor())
        {using var phase=RichImageTextPhase.Enter(Dispatcher);retained.RefreshImageActions();inlineImageView=retained.ImageView;return;}
        ClearInlineImageView();using var bootstrap=RichImageTextPhase.Enter(Dispatcher);BodyEditor.Visibility=Visibility.Collapsed;
        long generation=richImageGeneration,version=draft.EditVersion,previewEpoch=active.AttachmentPreviewEpoch;var original=draft.Document;var owner=workspace;bool bootstrapping=true;RichImageTextHost? created=null;
        bool Pure()=>!closed&&IsVisible&&ReferenceEquals(DataContext,draft)&&generation==richImageGeneration&&FoldToggle.IsChecked!=true&&ReferenceEquals(imageSession,active)&&!active.IsLocked&&ReferenceEquals(workspace,owner)&&active.Workspace.Notes.Contains(draft)&&draft is{IsClosed:false,IsDeleted:false,Mode:"rich",Document:{SchemaVersion:2}}&&ReferenceEquals(richImageHost,created)&&ReferenceEquals(StructuredHost.Content,created)&&(!bootstrapping||draft.EditVersion==version&&active.AttachmentPreviewEpoch==previewEpoch&&ReferenceEquals(draft.Document,original));
        bool Valid()=>Pure()&&current?.Invoke()==true&&Pure();
        try
        {
            created=new();richImageHost=created;StructuredHost.Content=created;
            if(!Valid()){ClearInlineImageView();RequestRichImageRefresh();return;}
            created.Initialize(active,draft,Valid,message=>{if(Valid())notice?.Invoke(message);},RequestRichImageRefresh);
            if(!Valid()||created.IsDisposed){ClearInlineImageView();RequestRichImageRefresh();return;}structuredEditor=created.Editor;inlineImageView=created.ImageView;
            StructuredHost.Visibility=Visibility.Visible;if(!Valid()){ClearInlineImageView();RequestRichImageRefresh();}else bootstrapping=false;
        }
        catch{created?.Dispose();if(ReferenceEquals(richImageHost,created))ClearInlineImageView();}
    }
    private bool CanInsertInlineImage()
    {
        if(closed||!IsVisible||FoldToggle.IsChecked==true||imageSession is not {IsLocked:false}||current?.Invoke()!=true||draft is not {Mode:"rich",IsClosed:false,IsDeleted:false,Document:{ } document}||!RichDocumentCodec.Inspect(document).Supported)return false;
        return document.SchemaVersion==2||structuredEditor is {IsDisposed:false};
    }
    private void ConfigureInlineImageInsertion(AttachmentPanel panel,SaveCoordinator active,Func<bool> valid)
    {
        panel.SetInlineImageInsertion(async(id,version,allowed)=>
        {
            if(!allowed()||!valid()||!CanInsertInlineImage()||draft.EditVersion!=version||!allowed())return false;
            int boundary;var document=draft.Document!;var caretEditor=document.SchemaVersion==1?structuredEditor:null;long caretGeneration=caretEditor?.CaretSelectionGeneration??0;
            if(draft.Document!.SchemaVersion==2){using var json=JsonDocument.Parse(draft.Document.SourceJson);boundary=json.RootElement.GetProperty("nodes").GetArrayLength();}
            else if(structuredEditor is null||!structuredEditor.TryGetCollapsedBlockBoundary(out boundary)){if(valid())notice?.Invoke("서식 문서의 한 블록 안에 빈 커서를 두세요. 선택한 본문에는 이미지를 추가하지 않습니다.");return false;}
            if(!allowed()||!ReferenceEquals(draft.Document,document)||caretEditor is not null&&(!ReferenceEquals(structuredEditor,caretEditor)||caretEditor.CaretSelectionGeneration!=caretGeneration))return false;
            bool applied=await active.InsertInlineImageAsync(draft,id,boundary,version);
            if(!valid()||!ReferenceEquals(imageSession,active))return false;
            if(applied)notice?.Invoke("이미지 위치를 추가했습니다. 문서의 이미지 표시 버튼으로 픽셀을 표시하세요. 자동 저장 상태를 확인하세요.");return applied;
        },()=>valid()&&CanInsertInlineImage(),()=>inlineImageView?.InvalidateImageDisplay());
    }
}

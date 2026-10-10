using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private RichImageDocumentView? inlineImageView;
    private NoteDraft? inlineImageNote;
    private RichImageTextHost? richImageHost;
    private long richImageGeneration;
    private bool richImageRefreshQueued;
    private Action? richImageWake;
    private void RichImageDataContextChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if(richImageHost is not null)ClearInlineImageView();
        if(ReferenceEquals(args.NewValue,SingleNote)&&ReferenceEquals(Editor.DataContext,SingleNote)&&SingleNote is{Mode:"rich",Document:{SchemaVersion:2}})RequestRichImageRefresh();
    }
    private void CancelRichImageRefresh()
    {
        var wake=richImageWake;richImageWake=null;richImageRefreshQueued=false;if(wake is not null)RichImageTextPhase.Unsubscribe(Dispatcher,wake);
    }
    private void RequestRichImageRefresh()
    {
        if(windowClosed||!IsVisible||concealing||session is not{IsLocked:false}||!ReferenceEquals(Editor.DataContext,SingleNote))return;
        long generation=richImageGeneration,epoch=uiEpoch;
        if(RichImageTextPhase.Busy(Dispatcher))
        {
            if(richImageWake is not null)return;
            var wake=RichImageTextPhase.WeakWake(Dispatcher,this,generation,epoch,static (root,completed,g,e)=>{if(!Equals(root.richImageWake,completed))return;root.richImageWake=null;if(g==root.richImageGeneration&&e==root.uiEpoch&&!root.windowClosed&&!root.concealing)root.RequestRichImageRefresh();});
            richImageWake=wake;RichImageTextPhase.Subscribe(Dispatcher,wake);return;
        }
        if(richImageRefreshQueued)return;richImageRefreshQueued=true;
        Dispatcher.BeginInvoke(RichImageTextPhase.WeakWork(this,generation,epoch,static (root,g,e)=>
        {
            if(g!=root.richImageGeneration||e!=root.uiEpoch)return;root.richImageRefreshQueued=false;
            if(root.windowClosed||!root.IsVisible||root.concealing||root.session is not{IsLocked:false}||!ReferenceEquals(root.Editor.DataContext,root.SingleNote))return;
            if(RichImageTextPhase.Busy(root.Dispatcher)){root.RequestRichImageRefresh();return;}root.RefreshNotes();
        }),System.Windows.Threading.DispatcherPriority.Background);
    }
    private void ClearInlineImageView()
    {
        using var phase=RichImageTextPhase.Enter(Dispatcher);CancelRichImageRefresh();richImageGeneration++;var owner=richImageHost;var previous=inlineImageView;richImageHost=null;inlineImageView=null;inlineImageNote=null;
        if(owner is not null){structuredEditor=null;structuredNote=null;}
        foreach(Action cleanup in new Action[]{()=>owner?.Dispose(),()=>previous?.Dispose(),()=>{if(owner is not null&&ReferenceEquals(StructuredHost.Content,owner)||previous is not null&&ReferenceEquals(StructuredHost.Content,previous))StructuredHost.Content=null;},()=>{if(owner is not null||previous is not null)StructuredHost.Visibility=Visibility.Collapsed;}})try{cleanup();}catch{}
    }
    private void ConfigureInlineImageView(SaveCoordinator active,NoteDraft selected)
    {
        if(RichImageTextPhase.Busy(Dispatcher)){RequestRichImageRefresh();return;}
        if(ReferenceEquals(inlineImageNote,selected)&&richImageHost is{IsDisposed:false} retained&&ReferenceEquals(StructuredHost.Content,retained)&&retained.RetainCurrentEditor())
        {using var phase=RichImageTextPhase.Enter(Dispatcher);retained.RefreshImageActions();inlineImageView=retained.ImageView;return;}
        ClearInlineImageView();using var bootstrap=RichImageTextPhase.Enter(Dispatcher);
        BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.IsUndoEnabled=false;BodyEditor.Clear();BodyEditor.IsReadOnly=true;BodyEditor.Visibility=Visibility.Collapsed;
        long epoch=uiEpoch,generation=richImageGeneration,version=selected.EditVersion,previewEpoch=active.AttachmentPreviewEpoch;var original=selected.Document;bool bootstrapping=true;RichImageTextHost? created=null;
        bool Pure()=>!windowClosed&&IsVisible&&!concealing&&epoch==uiEpoch&&generation==richImageGeneration&&ReferenceEquals(session,active)&&!active.IsLocked&&ReferenceEquals(SingleNote,selected)&&ReferenceEquals(Editor.DataContext,selected)&&active.Workspace.Notes.Contains(selected)&&selected is{IsClosed:false,IsDeleted:false,Mode:"rich",Document:{SchemaVersion:2}}&&ReferenceEquals(richImageHost,created)&&ReferenceEquals(StructuredHost.Content,created)&&(!bootstrapping||selected.EditVersion==version&&active.AttachmentPreviewEpoch==previewEpoch&&ReferenceEquals(selected.Document,original));
        try
        {
            created=new();richImageHost=created;inlineImageNote=selected;StructuredHost.Content=created;
            if(!Pure()){ClearInlineImageView();return;}
            created.Initialize(active,selected,Pure,message=>{if(Pure())Notice.Text=message;},RequestRichImageRefresh);
            if(!Pure()||created.IsDisposed){ClearInlineImageView();return;}
            structuredEditor=created.Editor;structuredNote=selected;inlineImageView=created.ImageView;
            StructuredHost.Visibility=Visibility.Visible;if(!Pure()){ClearInlineImageView();return;}created.Editor.ApplyPreferences(active.Workspace.GetUiDevice(uiDeviceId).Preferences);if(!Pure())ClearInlineImageView();else bootstrapping=false;
        }
        catch{created?.Dispose();if(ReferenceEquals(richImageHost,created))ClearInlineImageView();}
    }
    private bool CanInsertInlineImage(NoteDraft selected)
    {
        if(concealing||session is not {IsLocked:false}||!ReferenceEquals(SingleNote,selected)||selected is not {Mode:"rich",IsClosed:false,IsDeleted:false,Document:{ } document}||!RichDocumentCodec.Inspect(document).Supported)return false;
        return document.SchemaVersion==2||structuredEditor is {IsDisposed:false};
    }
    private void ConfigureInlineImageInsertion(AttachmentPanel panel,SaveCoordinator active,NoteDraft selected,Func<bool> current)
    {
        panel.SetInlineImageInsertion(async(id,version,allowed)=>
        {
            if(!allowed()||!current()||!CanInsertInlineImage(selected)||selected.EditVersion!=version||!allowed())return false;
            int boundary;var document=selected.Document!;var caretEditor=document.SchemaVersion==1?structuredEditor:null;long caretGeneration=caretEditor?.CaretSelectionGeneration??0;
            if(selected.Document!.SchemaVersion==2){using var json=JsonDocument.Parse(selected.Document.SourceJson);boundary=json.RootElement.GetProperty("nodes").GetArrayLength();}
            else if(structuredEditor is null||!structuredEditor.TryGetCollapsedBlockBoundary(out boundary)){if(current())Notice.Text="서식 문서의 한 블록 안에 빈 커서를 두세요. 선택한 본문에는 이미지를 추가하지 않습니다.";return false;}
            if(!allowed()||!ReferenceEquals(selected.Document,document)||caretEditor is not null&&(!ReferenceEquals(structuredEditor,caretEditor)||caretEditor.CaretSelectionGeneration!=caretGeneration))return false;
            bool applied=await active.InsertInlineImageAsync(selected,id,boundary,version);
            if(!current()||!ReferenceEquals(session,active))return false;
            if(applied)Notice.Text="이미지 위치를 문서에 추가했습니다. 픽셀 표시는 문서의 이미지 표시 버튼으로 실행하세요. 자동 저장 상태를 확인하세요.";
            return applied;
        },()=>current()&&CanInsertInlineImage(selected),()=>inlineImageView?.InvalidateImageDisplay());
    }
}

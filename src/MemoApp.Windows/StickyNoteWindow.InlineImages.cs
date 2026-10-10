using System.Text.Json;
using System.Windows;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
namespace MemoApp.Windows;
public partial class StickyNoteWindow
{
    private SaveCoordinator? imageSession;
    private RichImageDocumentView? inlineImageView;
    private void ClearInlineImageView()
    {
        var previous=inlineImageView;inlineImageView=null;previous?.Dispose();
        if(previous is not null&&ReferenceEquals(StructuredHost.Content,previous)){try{StructuredHost.Content=null;}catch{}try{StructuredHost.Visibility=Visibility.Collapsed;}catch{}}
    }
    private void ConfigureInlineImageView()
    {
        if(imageSession is not {IsLocked:false} active){BodyEditor.Text=draft.Text;BodyEditor.Visibility=Visibility.Visible;return;}
        BodyEditor.Visibility=Visibility.Collapsed;
        if(inlineImageView is null||inlineImageView.IsDisposed)
        {
            ClearInlineImageView();bool attached=false;RichImageDocumentView? created=null;
            bool Valid()=>!closed&&ReferenceEquals(imageSession,active)&&current?.Invoke()==true&&draft is {IsClosed:false,IsDeleted:false,Mode:"rich",Document:{SchemaVersion:2}}&&(!attached||ReferenceEquals(StructuredHost.Content,created));
            created=new(active,draft,Valid,message=>{if(Valid())notice?.Invoke(message);});
            if(Valid()){inlineImageView=created;StructuredHost.Content=created;attached=true;StructuredHost.Visibility=Visibility.Visible;}else created.Dispose();
        }
    }
    private bool CanInsertInlineImage()
    {
        if(closed||FoldToggle.IsChecked==true||imageSession is not {IsLocked:false}||current?.Invoke()!=true||draft is not {Mode:"rich",IsClosed:false,IsDeleted:false,Document:{ } document}||!RichDocumentCodec.Inspect(document).Supported)return false;
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

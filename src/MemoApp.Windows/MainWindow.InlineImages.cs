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
    private void ClearInlineImageView()
    {
        var previous=inlineImageView;inlineImageView=null;inlineImageNote=null;previous?.Dispose();
        if(previous is not null&&ReferenceEquals(StructuredHost.Content,previous)){try{StructuredHost.Content=null;}catch{}try{StructuredHost.Visibility=Visibility.Collapsed;}catch{}}
    }
    private void ConfigureInlineImageView(SaveCoordinator active,NoteDraft selected)
    {
        BindingOperations.ClearBinding(BodyEditor,TextBox.TextProperty);BodyEditor.IsUndoEnabled=false;BodyEditor.Clear();BodyEditor.IsReadOnly=true;BodyEditor.Visibility=Visibility.Collapsed;
        if(!ReferenceEquals(inlineImageNote,selected)||inlineImageView is null||inlineImageView.IsDisposed)
        {
            ClearInlineImageView();long epoch=uiEpoch;bool attached=false;RichImageDocumentView? created=null;
            bool Current()=>!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked&&ReferenceEquals(SingleNote,selected)&&ReferenceEquals(Editor.DataContext,selected)&&selected is {IsClosed:false,IsDeleted:false,Mode:"rich",Document:{SchemaVersion:2}}&&(!attached||ReferenceEquals(StructuredHost.Content,created));
            created=new(active,selected,Current,message=>{if(Current())Notice.Text=message;});
            if(Current()){inlineImageView=created;inlineImageNote=selected;StructuredHost.Content=created;attached=true;StructuredHost.Visibility=Visibility.Visible;}else created.Dispose();
        }
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

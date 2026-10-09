using System.ComponentModel;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private long searchPreviewGeneration;
    private bool clearingSearchPreview,buildingSearchPreview;
    private NoteDraft? searchPreviewNote;
    private Func<bool>? searchPreviewCurrent;
    private void ClearSearchResultPreview()
    {
        searchPreviewGeneration++;searchPreviewCurrent=null;
        if(searchPreviewNote is not null)searchPreviewNote.PropertyChanged-=SearchPreviewSourceChanged;
        searchPreviewNote=null;if(clearingSearchPreview)return;clearingSearchPreview=true;
        try
        {
            // Hide before native clearing; attempt every field even if one native callback throws.
            static void Clear(Action action){try{action();}catch{}}
            Clear(()=>SearchMatchArea.Visibility=Visibility.Collapsed);Clear(()=>SearchMatchPreview.Visibility=Visibility.Collapsed);
            Clear(()=>SearchMatchBefore.Text="");Clear(()=>SearchMatch.Text="");Clear(()=>SearchMatchAfter.Text="");Clear(()=>SearchMatchSource.Text="");
        }
        finally{clearingSearchPreview=false;}
    }
    private void SearchPreviewSourceChanged(object? sender,PropertyChangedEventArgs e)=>ClearSearchResultPreview();
    private void SearchPreviewRendering(object? sender,EventArgs e)
    {if(searchPreviewCurrent is { } current&&!current()){ClearSearchResultPreview();RenderSearchResultPreview();}}
    private void RenderSearchResultPreview()
    {
        ClearSearchResultPreview();if(clearingSearchPreview||buildingSearchPreview||concealing||session is not {IsLocked:false} active||SingleNote is not {IsClosed:false} note||SearchInput.Text.Length==0)return;
        buildingSearchPreview=true;
        try
        {
            long generation=searchPreviewGeneration,epoch=uiEpoch,version=note.EditVersion,attachmentEpoch=active.AttachmentPreviewEpoch;
            string query=SearchInput.Text;var field=(SearchField)Math.Max(0,SearchFieldFilter.SelectedIndex);
            bool Current()=>!concealing&&!windowClosed&&!confirmedExit&&ReferenceEquals(session,active)&&!active.IsLocked&&epoch==uiEpoch&&generation==searchPreviewGeneration&&version==note.EditVersion&&attachmentEpoch==active.AttachmentPreviewEpoch&&!note.IsClosed&&ReferenceEquals(SingleNote,note)&&active.Workspace.Notes.Contains(note)&&SearchInput.Text==query&&(SearchField)Math.Max(0,SearchFieldFilter.SelectedIndex)==field;
            SearchExcerpt? excerpt=null;string label="";
            if(field is SearchField.All or SearchField.Title){var title=SearchExcerpt.Project(note.Title,query);if(title.HasMatch){excerpt=title;label="제목";}}
            if(excerpt is null&&field is SearchField.All or SearchField.Body){var body=SearchExcerpt.Project(note.Text,query);if(body.HasMatch){excerpt=body;label="본문";}}
            if(excerpt is null&&field is SearchField.All or SearchField.Attachments)
                foreach(var item in active.Workspace.DescribeAttachments(note)){var file=SearchExcerpt.Project(item.Name,query);if(file.HasMatch){excerpt=file;label="첨부 이름";break;}}
            if(excerpt is null||!Current())return;
            searchPreviewNote=note;note.PropertyChanged+=SearchPreviewSourceChanged;searchPreviewCurrent=Current;
            SearchMatchSource.Text=label+" · 선택 결과의 일치 문장";if(!Current()){ClearSearchResultPreview();return;}
            SearchMatchBefore.Text=(excerpt.PrefixOmitted?"…":"")+excerpt.Before;if(!Current()){ClearSearchResultPreview();return;}
            SearchMatch.Text=excerpt.Match;if(!Current()){ClearSearchResultPreview();return;}
            SearchMatchAfter.Text=excerpt.After+(excerpt.SuffixOmitted?"…":"");if(!Current()){ClearSearchResultPreview();return;}
            SearchMatchPreview.Visibility=Visibility.Visible;if(!Current()){ClearSearchResultPreview();return;}
            SearchMatchArea.Visibility=Visibility.Visible;if(!Current())ClearSearchResultPreview();
        }
        catch{ClearSearchResultPreview();}
        finally{buildingSearchPreview=false;}
    }
}

using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public partial class MainWindow
{
    private sealed record SearchChoice(Guid Id,string Name);
    private sealed record FilterState(string Query,int Field,int View,int Sort,Guid? Folder,string Tag,DateTime? From,DateTime? Until);
    private bool searchBlocked,searchStateBusy;
    private Task recentTask=Task.CompletedTask;
    private NoteDraft? pendingRecent;
    private long recentGeneration;
    private FilterState ReadFilters()=>new(SearchInput.Text,SearchFieldFilter.SelectedIndex,ViewFilter.SelectedIndex,SortFilter.SelectedIndex,(FolderFilter.SelectedItem as FolderChoice)?.Id,TagFilter.Text,FromDate.SelectedDate,UntilDate.SelectedDate);
    private static SearchOptions Options(FilterState state)=>new(){Query=state.Query,Field=(SearchField)Math.Max(0,state.Field),Sort=(SearchSort)Math.Max(0,state.Sort),View=state.View switch{3=>SearchView.Archive,4=>SearchView.Trash,_=>SearchView.Active},FavoriteOnly=state.View==1,ImportantOnly=state.View==2,UnfiledOnly=state.View==5,FolderId=state.Folder,Tag=state.Tag.Length==0?null:state.Tag,ModifiedFrom=state.From is DateTime from?new DateTimeOffset(from.Date).ToUniversalTime():null,ModifiedUntil=state.Until is DateTime until?new DateTimeOffset(until.Date.AddDays(1)).ToUniversalTime():null};
    private bool LiveSearch(SaveCoordinator active,long epoch)=>!windowClosed&&!concealing&&epoch==uiEpoch&&ReferenceEquals(session,active)&&!active.IsLocked;
    private void ClearSearchStateViews()
    {
        recentGeneration++;pendingRecent=null;
        foreach(Action clear in new Action[]{()=>RecentNotesList.ItemsSource=null,()=>SavedSearchList.ItemsSource=null,()=>SavedSearchName.Clear(),()=>SavedSearchName.IsUndoEnabled=false})try{clear();}catch{}
    }
    private void RefreshSearchStateViews()
    {
        if(session is not{IsLocked:false} active||concealing)return;long epoch=uiEpoch;var device=active.Workspace.GetUiDevice(uiDeviceId);
        var recent=active.Workspace.RecentNotes(uiDeviceId).Select(n=>new SearchChoice(n.Id,n.Title)).ToArray();var saved=device.SavedSearches.Select(s=>new SearchChoice(s.Id,s.Name)).ToArray();
        bool Current()=>LiveSearch(active,epoch)&&active.Workspace.GetUiDevice(uiDeviceId)==device;
        if(!Current())return;RecentNotesList.ItemsSource=recent;if(!Current()){ClearSearchStateViews();return;}SavedSearchList.ItemsSource=saved;if(!Current()){ClearSearchStateViews();return;}SavedSearchName.IsUndoEnabled=true;if(!Current())ClearSearchStateViews();
    }
    private void QueueRecent(NoteDraft? note)
    {
        if(searchBlocked||concealing||session is not{IsLocked:false} active||note is not{IsClosed:false,IsDeleted:false}||active.Workspace.GetUiDevice(uiDeviceId).RecentNoteIds.FirstOrDefault()==note.Id)return;
        pendingRecent=note;recentGeneration++;if(recentTask.IsCompleted)recentTask=RecordRecentAsync();
    }
    private async Task RecordRecentAsync()
    {
        // One UI task and one replaceable selection; no background closure owns session or keys.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        while(pendingRecent is NoteDraft note)
        {
            pendingRecent=null;long generation=recentGeneration,epoch=uiEpoch;var active=session;long version=note.EditVersion;
            if(active is null||!LiveSearch(active,epoch))return;
            try
            {
                if(!await active.PrepareAttachmentsAsync())return;
                if(!LiveSearch(active,epoch))return;
                if(generation!=recentGeneration||!ReferenceEquals(SingleNote,note)||note.IsClosed||note.IsDeleted||note.EditVersion!=version||!active.Workspace.Notes.Contains(note))continue;
                active.Workspace.RecordRecentNote(uiDeviceId,note);if(LiveSearch(active,epoch))RefreshSearchStateViews();
            }
            catch{return;}
        }
    }
    private async void SaveSearch_Click(object sender,RoutedEventArgs e)=>await SaveCurrentSearchAsync();
    internal async Task<bool> SaveCurrentSearchAsync()
    {
        if(searchBlocked||searchStateBusy||session is not{IsLocked:false} active)return false;long epoch=uiEpoch;var filters=ReadFilters();string name=SavedSearchName.Text;searchStateBusy=true;
        try
        {
            if(!await active.PrepareAttachmentsAsync()||!LiveSearch(active,epoch)||searchBlocked||filters!=ReadFilters()||name!=SavedSearchName.Text)return false;
            if(filters.Folder is Guid folder&&!active.Workspace.Folders.Any(f=>f.FolderId==folder))return false;
            active.Workspace.SaveSearch(uiDeviceId,name,Options(filters));if(!LiveSearch(active,epoch))return false;RefreshSearchStateViews();Notice.Text="검색 조건을 암호 저장 대상에 추가했습니다.";return true;
        }
        catch{if(LiveSearch(active,epoch))Notice.Text="조건 저장 실패 — 이름·조건·저장 용량을 확인하세요.";return false;}
        finally{searchStateBusy=false;}
    }
    private void ApplySearch_Click(object sender,RoutedEventArgs e){if(SavedSearchList.SelectedItem is SearchChoice choice)ApplySavedSearch(choice.Id);}
    private void DeleteSearch_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active||SavedSearchList.SelectedItem is not SearchChoice choice)return;
        try{active.Workspace.RemoveSavedSearch(uiDeviceId,choice.Id);RefreshSearchStateViews();}catch{Notice.Text="저장 검색을 지우지 못했습니다.";}
    }
    private void RecentOpen_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active||RecentNotesList.SelectedItem is not SearchChoice choice)return;
        var note=active.Workspace.RecentNotes(uiDeviceId).FirstOrDefault(n=>n.Id==choice.Id);if(note is not null){active.Workspace.RecordRecentNote(uiDeviceId,note);if(!active.IsLocked&&!concealing)OpenSticky(note);}
    }
    private void RecentClear_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active)return;try{active.Workspace.ClearRecentNotes(uiDeviceId);RefreshSearchStateViews();}catch{Notice.Text="최근 메모 목록을 지우지 못했습니다.";}
    }
    private bool TryRepresent(SearchOptions options,out FilterState filters)
    {
        filters=new(options.Query,(int)options.Field,options.View switch{SearchView.Archive=>3,SearchView.Trash=>4,_=>options.FavoriteOnly?1:options.ImportantOnly?2:options.UnfiledOnly?5:0},(int)options.Sort,options.FolderId,options.Tag??"",options.ModifiedFrom?.LocalDateTime.Date,options.ModifiedUntil?.LocalDateTime.Date.AddDays(-1));
        return Options(filters)==options;
    }
    private void ClearSearchResults()
    {
        // Detach the source before native clearing can issue binding writes.
        Editor.DataContext=null;ClearSearchResultPreview();ClearMarkdownPreview();ClearStructuredEditor();ClearAttachmentPanel();NotesList.ItemsSource=null;Editor.IsEnabled=false;
    }
    private void WriteFilters(FilterState state,Func<bool> current)
    {
        void Set(Action action){if(!current())throw new InvalidOperationException();action();if(!current())throw new InvalidOperationException();}
        Set(()=>SearchInput.Text=state.Query);Set(()=>SearchFieldFilter.SelectedIndex=state.Field);Set(()=>ViewFilter.SelectedIndex=state.View);Set(()=>SortFilter.SelectedIndex=state.Sort);
        Set(()=>FolderFilter.SelectedItem=FolderFilter.Items.Cast<FolderChoice>().Single(f=>f.Id==state.Folder));Set(()=>TagFilter.Text=state.Tag);Set(()=>FromDate.SelectedDate=state.From);Set(()=>UntilDate.SelectedDate=state.Until);
        if(ReadFilters()!=state)throw new InvalidOperationException();
    }
    internal bool ApplySavedSearch(Guid id)
    {
        if(searchBlocked||session is not{IsLocked:false} active)return false;long epoch=uiEpoch;var saved=active.Workspace.GetUiDevice(uiDeviceId).SavedSearches.FirstOrDefault(s=>s.Id==id);
        bool FolderExists(Guid? folder)=>folder is null||active.Workspace.Folders.Any(f=>f.FolderId==folder);
        FilterState target;
        try{if(saved is null||!FolderExists(saved.Options.FolderId)||!TryRepresent(saved.Options,out target)){Notice.Text="저장 조건을 정확히 적용할 수 없습니다. 폴더와 조건을 확인하세요.";return false;}}catch{return false;}
        var before=ReadFilters();bool previousLoading=loadingUi;searchBlocked=true;loadingUi=true;
        bool Current()=>LiveSearch(active,epoch)&&active.Workspace.GetUiDevice(uiDeviceId).SavedSearches.Any(s=>s==saved)&&FolderExists(target.Folder);
        try
        {
            ClearSearchResults();WriteFilters(target,Current);if(!Current()||Options(ReadFilters())!=saved.Options)throw new InvalidOperationException();searchBlocked=false;return true;
        }
        catch
        {
            if(LiveSearch(active,epoch))try{WriteFilters(before,()=>LiveSearch(active,epoch)&&FolderExists(before.Folder));searchBlocked=false;}catch{}
            if(LiveSearch(active,epoch))Notice.Text=searchBlocked?"검색 조건을 복원하지 못했습니다. 검색 초기화를 눌러 주세요.":"검색 조건을 적용하지 않았습니다. 원래 조건을 복원했습니다.";
            return false;
        }
        finally{loadingUi=previousLoading;if(LiveSearch(active,epoch)&&!searchBlocked)RefreshNotes();}
    }
    private void ResetSearch_Click(object sender,RoutedEventArgs e)
    {
        if(session is not{IsLocked:false} active)return;long epoch=uiEpoch;bool previousLoading=loadingUi;searchBlocked=true;loadingUi=true;
        try{ClearSearchResults();WriteFilters(new("",0,0,0,null,"",null,null),()=>LiveSearch(active,epoch));searchBlocked=false;}
        catch{if(LiveSearch(active,epoch))Notice.Text="검색 초기화 실패 — 다시 시도하세요.";}
        finally{loadingUi=previousLoading;if(LiveSearch(active,epoch)&&!searchBlocked)RefreshNotes();}
    }
}

using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoApp.Core.Editing;

namespace MemoApp.Windows;

// Inert, bounded immutable text comparison. No document, image, link or file-path renderer.
public sealed class BackupMergeWindow:Window,IDisposable
{
    private Func<bool>? current;
    private Func<Task<bool>>? preserve,resolve;
    private Func<BackupMergeBranchComparison?,Task<bool>>? previewResolution;
    private Action? invalidateResolution;private bool expectedClearSelection;private long selectionGeneration;
    internal Task WhenResolutionSelectionIdle{get;private set;}=Task.CompletedTask;
    private bool revoked,closing,closed,busy;
    private readonly TextBlock counts=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock currentInfo=new(){TextWrapping=TextWrapping.Wrap},incomingInfo=new(){TextWrapping=TextWrapping.Wrap};
    public ListBox BranchesList{get;}=new(){DisplayMemberPath="Incoming.Title",MaxHeight=180,Margin=new(0,8,0,8)};
    public TextBox CurrentTitleView{get;}=Text(false);
    public TextBox CurrentTextView{get;}=Text(true);
    public TextBox IncomingTitleView{get;}=Text(false);
    public TextBox IncomingTextView{get;}=Text(true);
    public Button PreserveButton{get;}=new(){Content="현재 메모를 유지하고 백업 이력 보존",IsEnabled=false,Margin=new(0,12,0,0)};
    public Button ResolveButton{get;}=new(){Content="현재 내용을 유지하고 선택 분기 해결",IsEnabled=false,Margin=new(0,12,0,0)};
    public bool IsRevoked=>revoked;

    private static TextBox Text(bool multiline)=>new(){IsReadOnly=true,IsUndoEnabled=false,MaxLength=256,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?80:0,Margin=new(0,4,0,8)};
    public BackupMergeWindow(Func<bool> current,bool mergePreview)
    {
        ArgumentNullException.ThrowIfNull(current);this.current=current;
        Title=mergePreview?"같은 메모 ID의 백업 이력 보존":"보존된 백업 분기 비교";Width=700;Height=700;
        var panel=new StackPanel{Margin=new(16)};
        panel.Children.Add(new TextBlock{Text=mergePreview?"현재 메모와 내용은 유지합니다. 선택한 백업의 같은 메모 ID 이력을 보존합니다. 삭제 메모·백업에 없는 선택 메모는 전체 거부합니다. 새 복사 복구와 별도 작업입니다.":"현재 메모와 보존된 분기를 비교합니다. 분기를 직접 선택한 뒤 현재 내용을 그대로 유지하며 확인 완료로 기록할 수 있습니다. 두 수정본은 이력에 남고 다른 미해결 분기는 유지합니다. 적용 전 현재 암호문을 보존합니다.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="제목/본문 발췌 최대256자 · 원문 서식·첨부는 자동으로 열지 않음 · 시각/목록 순서는 인과관계나 최신 순위를 뜻하지 않음",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)});
        panel.Children.Add(counts);panel.Children.Add(BranchesList);
        panel.Children.Add(new TextBlock{Text="현재 메모",FontWeight=FontWeights.Bold});panel.Children.Add(currentInfo);panel.Children.Add(CurrentTitleView);panel.Children.Add(CurrentTextView);
        panel.Children.Add(new TextBlock{Text=mergePreview?"백업에서 보존할 분기":"보존된 백업 분기",FontWeight=FontWeights.Bold});panel.Children.Add(incomingInfo);panel.Children.Add(IncomingTitleView);panel.Children.Add(IncomingTextView);
        PreserveButton.Visibility=mergePreview?Visibility.Visible:Visibility.Collapsed;panel.Children.Add(PreserveButton);ResolveButton.Visibility=mergePreview?Visibility.Collapsed:Visibility.Visible;panel.Children.Add(ResolveButton);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        BranchesList.SelectionChanged+=Selected;PreserveButton.Click+=Preserve;ResolveButton.Click+=Resolve;CompositionTarget.Rendering+=Rendering;
        Closing+=(_,_)=>{closing=true;Revoke();};Closed+=(_,_)=>{closed=true;Revoke();};
    }
    private bool Live()=>!revoked&&!closed&&current?.Invoke()==true;
    private bool Check(){if(Live())return true;Dispose();return false;}
    public bool Publish(ImmutableArray<BackupMergeBranchComparison> details,int selectedCount,int importedCount,bool mergePreview)
    {
        Dispatcher.VerifyAccess();if(!Check())return false;
        static bool Bounded(BackupMergeBranchContent c)=>c is not null&&c.RevisionId!=Guid.Empty&&c.Title is not null&&c.TextExcerpt is not null&&c.Title.Length<=256&&c.TextExcerpt.Length<=256&&(c.Mode is "plain" or "rich" or "markdown")&&c.AttachmentCount is >=0 and <=16;
        if(details.IsDefault||details.Length>10000||selectedCount is <1 or >100||importedCount is <0 or >10000||details.Any(d=>d is null||d.NoteId==Guid.Empty||!Bounded(d.Current)||!Bounded(d.Incoming))){Dispose();return false;}
        try
        {
            counts.Text=mergePreview?$"선택 메모 {selectedCount}개 · 추가 이력 {importedCount}개 · 보존 후 미해결 분기 {details.Length}개":"미해결 백업 분기 "+details.Length+"개";if(!Check())return false;
            BranchesList.ItemsSource=details;if(!Check())return false;BranchesList.SelectedIndex=mergePreview&&details.Length>0?0:-1;if(!Check())return false;
            Show();return Check();
        }
        catch{Dispose();return false;}
    }
    private void Selected(object sender,SelectionChangedEventArgs e)
    {
        // Suppress exactly the expected removal event from our own publication clear.
        // Nested selection callbacks cannot borrow this one-use allowance.
        if(expectedClearSelection&&BranchesList.SelectedItem is null&&BranchesList.Items.Count==0){expectedClearSelection=false;return;}
        selectionGeneration++;invalidateResolution?.Invoke();if(!Check())return;
        var branch=BranchesList.SelectedItem as BackupMergeBranchComparison;long generation=selectionGeneration;
        try
        {
            ResolveButton.IsEnabled=false;if(!SelectedCurrent(branch,generation))return;
            if(!SetComparison(branch,generation))return;
            if(previewResolution is{ } preview)WhenResolutionSelectionIdle=ObserveResolutionSelectionAsync(preview(branch));
        }
        catch{Dispose();}
    }
    private bool SelectedCurrent(BackupMergeBranchComparison? branch,long generation)=>Check()&&generation==selectionGeneration&&ReferenceEquals(BranchesList.SelectedItem,branch);
    private bool SetComparison(BackupMergeBranchComparison? branch,long generation)
    {
        CurrentTitleView.Text=branch?.Current.Title??"";if(!SelectedCurrent(branch,generation))return false;
        CurrentTextView.Text=branch?.Current.TextExcerpt??"";if(!SelectedCurrent(branch,generation))return false;
        IncomingTitleView.Text=branch?.Incoming.Title??"";if(!SelectedCurrent(branch,generation))return false;
        IncomingTextView.Text=branch?.Incoming.TextExcerpt??"";if(!SelectedCurrent(branch,generation))return false;
        currentInfo.Text=Info(branch?.Current);if(!SelectedCurrent(branch,generation))return false;
        incomingInfo.Text=Info(branch?.Incoming);return SelectedCurrent(branch,generation);
    }
    private async Task ObserveResolutionSelectionAsync(Task<bool> action)
    {try{await action;}catch{Dispose();}}
    internal void ConfigureResolution(Action invalidate,Func<BackupMergeBranchComparison?,Task<bool>> preview,Func<Task<bool>> action)
    {Dispatcher.VerifyAccess();if(!Check())return;invalidateResolution=invalidate;previewResolution=preview;resolve=action;ResolveButton.IsEnabled=false;Check();}
    internal bool PublishResolutionComparison(BackupMergeBranchComparison comparison)
    {
        Dispatcher.VerifyAccess();if(!Check()||BranchesList.SelectedItem is not BackupMergeBranchComparison selected||selected.NoteId!=comparison.NoteId||selected.Incoming.RevisionId!=comparison.Incoming.RevisionId)return false;
        try
        {
            // Keep the selected inert object identity; use the coordinator's exact fresh bounded display values.
            long generation=selectionGeneration;
            CurrentTitleView.Text=comparison.Current.Title;if(!SelectedCurrent(selected,generation))return false;
            CurrentTextView.Text=comparison.Current.TextExcerpt;if(!SelectedCurrent(selected,generation))return false;
            IncomingTitleView.Text=comparison.Incoming.Title;if(!SelectedCurrent(selected,generation))return false;
            IncomingTextView.Text=comparison.Incoming.TextExcerpt;if(!SelectedCurrent(selected,generation))return false;
            currentInfo.Text=Info(comparison.Current);if(!SelectedCurrent(selected,generation))return false;
            incomingInfo.Text=Info(comparison.Incoming);return SelectedCurrent(selected,generation);
        }
        catch{Dispose();return false;}
    }
    internal bool SetResolutionReady(bool ready)
    {Dispatcher.VerifyAccess();if(!Check())return false;try{ResolveButton.IsEnabled=ready&&!busy;return Check();}catch{Dispose();return false;}}
    internal bool ClearResolutionPublication()
    {
        Dispatcher.VerifyAccess();bool cleared=true;
        foreach(Action clear in new Action[]{()=>Hide(),()=>ResolveButton.IsEnabled=false,()=>CurrentTitleView.Clear(),()=>CurrentTextView.Clear(),()=>IncomingTitleView.Clear(),()=>IncomingTextView.Clear(),()=>currentInfo.Text="",()=>incomingInfo.Text="",()=>counts.Text=""})try{clear();}catch{cleared=false;}
        try{expectedClearSelection=true;BranchesList.ItemsSource=null;if(BranchesList.SelectedItem is not null||BranchesList.Items.Count!=0)cleared=false;}catch{cleared=false;}finally{expectedClearSelection=false;}
        return cleared&&!revoked;
    }
    private async void Resolve(object sender,RoutedEventArgs e)
    {
        if(!Check()||busy||!ResolveButton.IsEnabled||resolve is not{ } action)return;long generation=selectionGeneration;busy=true;
        try{ResolveButton.IsEnabled=false;if(Check())await action();}catch{Dispose();}
        finally{busy=false;if(!revoked&&generation==selectionGeneration)try{ResolveButton.IsEnabled=true;Check();}catch{Dispose();}}
    }
    private static string Info(BackupMergeBranchContent? item)=>item is null?"":$"모드 {item.Mode} · 첨부 {item.AttachmentCount}개 · 기록 시각 {item.ModifiedAt:O}"+(item.Truncated?" · 본문 발췌":"");
    internal void ConfigurePreserve(Func<Task<bool>> action)
    {Dispatcher.VerifyAccess();if(!Check())return;preserve=action;PreserveButton.IsEnabled=!busy;Check();}
    private async void Preserve(object sender,RoutedEventArgs e)
    {
        if(!Check()||busy||preserve is not{ } action)return;busy=true;
        try{PreserveButton.IsEnabled=false;if(Check())await action();}catch{Dispose();}
        finally{busy=false;if(!revoked)try{PreserveButton.IsEnabled=preserve is not null;Check();}catch{Dispose();}}
    }
    private void Rendering(object? sender,EventArgs e){if(!Live())Dispose();}
    private void Revoke()
    {
        if(revoked)return;revoked=true;current=null;preserve=null;resolve=null;previewResolution=null;invalidateResolution=null;CompositionTarget.Rendering-=Rendering;BranchesList.SelectionChanged-=Selected;PreserveButton.Click-=Preserve;ResolveButton.Click-=Resolve;
        // Each native clear is independent; one failing setter cannot keep the other fields alive.
        foreach(Action clear in new Action[]{()=>Hide(),()=>Owner=null,()=>BranchesList.SelectedItem=null,()=>BranchesList.ItemsSource=null,()=>CurrentTitleView.Clear(),()=>CurrentTextView.Clear(),()=>IncomingTitleView.Clear(),()=>IncomingTextView.Clear(),()=>currentInfo.Text="",()=>incomingInfo.Text="",()=>counts.Text="",()=>PreserveButton.IsEnabled=false,()=>ResolveButton.IsEnabled=false})try{clear();}catch{}
    }
    public void Dispose(){Dispatcher.VerifyAccess();Revoke();if(!closing&&!closed)try{Close();}catch{}}
}

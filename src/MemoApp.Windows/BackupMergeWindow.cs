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
    private Func<Task<bool>>? preserve;
    private bool revoked,closing,closed,busy;
    private readonly TextBlock counts=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock currentInfo=new(){TextWrapping=TextWrapping.Wrap},incomingInfo=new(){TextWrapping=TextWrapping.Wrap};
    public ListBox BranchesList{get;}=new(){DisplayMemberPath="Incoming.Title",MaxHeight=180,Margin=new(0,8,0,8)};
    public TextBox CurrentTitleView{get;}=Text(false);
    public TextBox CurrentTextView{get;}=Text(true);
    public TextBox IncomingTitleView{get;}=Text(false);
    public TextBox IncomingTextView{get;}=Text(true);
    public Button PreserveButton{get;}=new(){Content="현재 메모를 유지하고 백업 이력 보존",IsEnabled=false,Margin=new(0,12,0,0)};
    public bool IsRevoked=>revoked;

    private static TextBox Text(bool multiline)=>new(){IsReadOnly=true,IsUndoEnabled=false,MaxLength=256,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?80:0,Margin=new(0,4,0,8)};
    public BackupMergeWindow(Func<bool> current,bool mergePreview)
    {
        ArgumentNullException.ThrowIfNull(current);this.current=current;
        Title=mergePreview?"같은 메모 ID의 백업 이력 보존":"보존된 백업 분기 비교";Width=700;Height=700;
        var panel=new StackPanel{Margin=new(16)};
        panel.Children.Add(new TextBlock{Text=mergePreview?"현재 메모와 내용은 유지합니다. 선택한 백업의 같은 메모 ID 이력을 보존합니다. 삭제 메모·백업에 없는 선택 메모는 전체 거부합니다. 새 복사 복구와 별도 작업입니다.":"현재 메모와 보존된 백업 분기를 읽기 전용으로 비교합니다. 현재 메모 교체·자동 복원·분기 해결 기능은 제공하지 않습니다.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="제목/본문 발췌 최대256자 · 원문 서식·첨부는 자동으로 열지 않음 · 시각/목록 순서는 인과관계나 최신 순위를 뜻하지 않음",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)});
        panel.Children.Add(counts);panel.Children.Add(BranchesList);
        panel.Children.Add(new TextBlock{Text="현재 메모",FontWeight=FontWeights.Bold});panel.Children.Add(currentInfo);panel.Children.Add(CurrentTitleView);panel.Children.Add(CurrentTextView);
        panel.Children.Add(new TextBlock{Text=mergePreview?"백업에서 보존할 분기":"보존된 백업 분기",FontWeight=FontWeights.Bold});panel.Children.Add(incomingInfo);panel.Children.Add(IncomingTitleView);panel.Children.Add(IncomingTextView);
        PreserveButton.Visibility=mergePreview?Visibility.Visible:Visibility.Collapsed;panel.Children.Add(PreserveButton);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        BranchesList.SelectionChanged+=Selected;PreserveButton.Click+=Preserve;CompositionTarget.Rendering+=Rendering;
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
            BranchesList.ItemsSource=details;if(!Check())return false;BranchesList.SelectedIndex=details.Length>0?0:-1;if(!Check())return false;
            Show();return Check();
        }
        catch{Dispose();return false;}
    }
    private void Selected(object sender,SelectionChangedEventArgs e)
    {
        if(!Check())return;
        try
        {
            var branch=BranchesList.SelectedItem as BackupMergeBranchComparison;
            CurrentTitleView.Text=branch?.Current.Title??"";if(!Check())return;
            CurrentTextView.Text=branch?.Current.TextExcerpt??"";if(!Check())return;
            IncomingTitleView.Text=branch?.Incoming.Title??"";if(!Check())return;
            IncomingTextView.Text=branch?.Incoming.TextExcerpt??"";if(!Check())return;
            currentInfo.Text=Info(branch?.Current);if(!Check())return;incomingInfo.Text=Info(branch?.Incoming);Check();
        }
        catch{Dispose();}
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
        if(revoked)return;revoked=true;current=null;preserve=null;CompositionTarget.Rendering-=Rendering;BranchesList.SelectionChanged-=Selected;PreserveButton.Click-=Preserve;
        // Each native clear is independent; one failing setter cannot keep the other fields alive.
        foreach(Action clear in new Action[]{()=>Hide(),()=>Owner=null,()=>BranchesList.SelectedItem=null,()=>BranchesList.ItemsSource=null,()=>CurrentTitleView.Clear(),()=>CurrentTextView.Clear(),()=>IncomingTitleView.Clear(),()=>IncomingTextView.Clear(),()=>currentInfo.Text="",()=>incomingInfo.Text="",()=>counts.Text="",()=>PreserveButton.IsEnabled=false})try{clear();}catch{}
    }
    public void Dispose(){Dispatcher.VerifyAccess();Revoke();if(!closing&&!closed)try{Close();}catch{}}
}

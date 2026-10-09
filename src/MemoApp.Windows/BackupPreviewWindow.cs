using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoApp.Core.Storage;
namespace MemoApp.Windows;
public sealed class BackupPreviewWindow : Window,IDisposable
{
    private Func<bool>? current;
    private bool revoked,closing,closed;
    private readonly TextBlock counts=new(){TextWrapping=TextWrapping.Wrap};
    public ListBox NotesList{get;}=new(){DisplayMemberPath="Title",MaxHeight=280,Margin=new(0,8,0,8)};
    public TextBox TitleView{get;}=new(){IsReadOnly=true,IsUndoEnabled=false,MaxLength=256};
    public TextBox ExcerptView{get;}=new(){IsReadOnly=true,IsUndoEnabled=false,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxLength=256,MinHeight=100,Margin=new(0,8,0,0)};
    public bool IsRevoked=>revoked;
    public BackupPreviewWindow(Func<bool> current)
    {
        this.current=current;Title="암호 백업 미리보기";Width=620;Height=580;
        var panel=new StackPanel{Margin=new(16)};panel.Children.Add(new TextBlock{Text="읽기 전용 · 제목/본문 최대256자 · 복구/원본 파일 변경 없음",TextWrapping=TextWrapping.Wrap});panel.Children.Add(counts);panel.Children.Add(NotesList);panel.Children.Add(TitleView);panel.Children.Add(ExcerptView);Content=panel;
        NotesList.SelectionChanged+=Selected;CompositionTarget.Rendering+=Rendering;
        Closing+=(_,_)=>{closing=true;Revoke();};Closed+=(_,_)=>{closed=true;Revoke();};
    }
    private bool Live()=>!revoked&&!closed&&current?.Invoke()==true;
    private bool Check(){if(Live())return true;Dispose();return false;}
    public bool Publish(EncryptedBackupPreview data)
    {
        Dispatcher.VerifyAccess();if(!Check())return false;
        if(data.Notes is null||data.Notes.Length>100||data.Notes.Any(n=>n is null||n.Title is null||n.Excerpt is null||n.Title.Length>256||n.Excerpt.Length>256)){Dispose();return false;}
        try
        {
            counts.Text=$"저장 순서 {data.Sequence} · 메모 {data.Notes.Length} · 이력 {data.HistoryCount} · 폴더 {data.FolderCount} · 태그 {data.TagCount}";if(!Check())return false;
            NotesList.ItemsSource=data.Notes;if(!Check())return false;NotesList.SelectedIndex=data.Notes.Length>0?0:-1;if(!Check())return false;
            Show();return Check();
        }
        catch{Dispose();return false;}
    }
    private void Selected(object sender,SelectionChangedEventArgs e)
    {
        if(!Check())return;
        try
        {
            var item=NotesList.SelectedItem as BackupNotePreview;TitleView.Text=item?.Title??"";if(!Check())return;
            ExcerptView.Text=item?.Excerpt??"";Check();
        }
        catch{Dispose();}
    }
    private void Rendering(object? sender,EventArgs e){if(!Live())Dispose();}
    private void Revoke()
    {
        if(revoked)return;revoked=true;current=null;CompositionTarget.Rendering-=Rendering;NotesList.SelectionChanged-=Selected;
        try{Hide();}catch{}try{Owner=null;}catch{}try{NotesList.SelectedItem=null;}catch{}try{NotesList.ItemsSource=null;}catch{}
        try{TitleView.Clear();}catch{}try{ExcerptView.Clear();}catch{}try{counts.Text="";}catch{}
    }
    public void Dispose(){Dispatcher.VerifyAccess();Revoke();if(!closing&&!closed)try{Close();}catch{}}
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using Microsoft.Win32;
namespace MemoApp.Windows;

// Opaque original files only. This panel never decodes, launches or exports content.
public sealed class AttachmentPanel : UserControl,IDisposable
{
    private SaveCoordinator? session;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private readonly CancellationTokenSource cancellation=new();
    private readonly Button add=new(){Content="파일 첨부",Padding=new(6,3,6,3),Margin=new(0,0,6,0)};
    private readonly Button detach=new(){Content="선택 첨부 분리",Padding=new(6,3,6,3),IsEnabled=false};
    private bool busy,refreshing,refreshPending;
    private sealed record Entry(Guid Id,string Label);
    public ListBox FilesList{get;}=new(){MaxHeight=110,MinHeight=24,DisplayMemberPath="Label",Margin=new(0,4,0,0)};
    public bool IsDisposed{get;private set;}
    public AttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice)
    {
        this.session=session;this.note=note;this.current=current;this.notice=notice;
        var content=new StackPanel();var buttons=new WrapPanel();buttons.Children.Add(add);buttons.Children.Add(detach);content.Children.Add(buttons);
        content.Children.Add(new TextBlock{Text="원본 파일 4 MiB · 메모당 16개 · 보존 파일 합계 8 MiB · 자동 실행 없음",TextWrapping=TextWrapping.Wrap});content.Children.Add(FilesList);Content=content;
        add.Click+=AddClicked;detach.Click+=DetachClicked;FilesList.SelectionChanged+=SelectionChanged;
        session.Workspace.Changed+=Refresh;session.Conceal+=Dispose;note.PropertyChanged+=NoteChanged;Refresh();
    }
    private bool Current()=>!IsDisposed&&session is {IsLocked:false} active&&note is {IsClosed:false,IsDeleted:false} source&&active.Workspace.Notes.Contains(source)&&current?.Invoke()==true;
    private bool Same(SaveCoordinator active,NoteDraft source,long version)=>Current()&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&source.EditVersion==version;
    private void Report(string message){if(Current())notice?.Invoke(message);}
    private void NoteChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName is nameof(NoteDraft.IsClosed) or nameof(NoteDraft.IsDeleted)){if(!Current())Dispose();}}
    private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>detach.IsEnabled=Current()&&!busy&&FilesList.SelectedItem is Entry;
    private async void AddClicked(object sender,RoutedEventArgs e)
    {
        await ImportFileAsync(()=>
        {
            var owner=Window.GetWindow(this);if(owner is null)return null;
            var dialog=new OpenFileDialog{Filter="원본 파일 (내용을 실행하지 않음)|*.*",CheckFileExists=true,Multiselect=false};
            return dialog.ShowDialog(owner)==true?dialog.FileName:null;
        });
    }
    private void DetachClicked(object sender,RoutedEventArgs e)=>DetachSelected();
    public async Task<bool> ImportFileAsync(Func<string?> choose)
    {
        if(!Current()||busy)return false;var active=session!;var sourceNote=note!;long version=sourceNote.EditVersion;var token=cancellation.Token;busy=true;Refresh();
        try
        {
            if(!Same(active,sourceNote,version))return false;
            string? path=choose();if(path is null||!Same(active,sourceNote,version))return false;
            if(!await active.PrepareAttachmentsAsync()||!Same(active,sourceNote,version))return false;
            token.ThrowIfCancellationRequested();
            using(var input=await Task.Run(()=>AttachmentSource.Read(path,token),token))
            {
                if(!Same(active,sourceNote,version)||token.IsCancellationRequested)return false;
                active.AttachBytes(sourceNote,input.Content,input.Name,input.Mime,version);
            }
            if(!Current()||!ReferenceEquals(session,active))return false;
            bool saved=await active.SaveAsync();if(!Current()||!ReferenceEquals(session,active))return false;
            Report(saved&&!active.IsDirty?"원본 파일을 암호화해 저장했습니다. 원본은 바꾸지 않았습니다.":"첨부 변경을 반영했지만 저장 완료를 확인하지 못했습니다. 관리창 저장·복구 상태를 확인하세요.");return saved;
        }
        catch(OperationCanceledException){return false;}
        catch{Report("파일 첨부 실패 — 로컬 일반 파일·4 MiB/16개/전체 저장·이력 한도를 확인하세요. 원본 파일은 바꾸지 않았습니다.");return false;}
        finally{busy=false;if(!IsDisposed)Refresh();}
    }
    public bool DetachSelected()
    {
        if(!Current()||busy||FilesList.SelectedItem is not Entry selected)return false;var active=session!;var source=note!;long version=source.EditVersion;
        try
        {
            if(!Same(active,source,version)||!source.AttachmentIds.Contains(selected.Id))return false;
            active.DetachAttachment(source,selected.Id,version);Report("현재 메모의 첨부 연결을 분리했습니다. 원본 암호 객체는 이력·백업에 보존하며 자동 저장 상태를 확인하세요.");return true;
        }
        catch{Report("첨부 분리를 적용하지 않았습니다. 저장 상태·이력 한도를 확인하세요.");return false;}
    }
    private void Refresh()
    {
        if(IsDisposed)return;if(!Current()){Dispose();return;}
        if(refreshing){refreshPending=true;return;}refreshing=true;
        try
        {
            var active=session!;var source=note!;long version=source.EditVersion;
            Guid? selected=(FilesList.SelectedItem as Entry)?.Id;
            var entries=active.Workspace.DescribeAttachments(source).Select(item=>new Entry(item.Id,$"{item.Name} · {item.Length:N0} bytes")).ToArray();
            if(!Same(active,source,version))return;FilesList.ItemsSource=entries;
            if(!Same(active,source,version)){ClearLabels();return;}
            FilesList.SelectedItem=entries.FirstOrDefault(item=>item.Id==selected);
            if(!Same(active,source,version)){ClearLabels();return;}
            add.IsEnabled=!busy;detach.IsEnabled=!busy&&FilesList.SelectedItem is Entry;
        }
        catch{if(!IsDisposed)ClearLabels();}
        finally
        {
            refreshing=false;if(refreshPending&&!IsDisposed){refreshPending=false;Dispatcher.BeginInvoke(new Action(()=>{if(!IsDisposed)Refresh();}),DispatcherPriority.Background);}
        }
    }
    private void ClearLabels()
    {
        try{FilesList.SelectedItem=null;}catch{}
        try{FilesList.ItemsSource=null;}catch{}
        try{add.IsEnabled=false;}catch{}
        try{detach.IsEnabled=false;}catch{}
    }
    public void Dispose()
    {
        if(IsDisposed)return;IsDisposed=true;var active=session;var source=note;session=null;note=null;current=null;notice=null;refreshPending=false;
        if(active is not null){active.Workspace.Changed-=Refresh;active.Conceal-=Dispose;}if(source is not null)source.PropertyChanged-=NoteChanged;
        // Conceal this host before native collection callbacks; parents also conceal independently.
        try{Visibility=Visibility.Collapsed;}catch{}
        try{Content=null;}catch{}
        cancellation.Cancel();ClearLabels();add.Click-=AddClicked;detach.Click-=DetachClicked;FilesList.SelectionChanged-=SelectionChanged;cancellation.Dispose();
    }
}

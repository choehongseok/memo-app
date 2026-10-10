using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;
public sealed partial class AttachmentPanel
{
    private readonly Guid filePathProfile;
    private long filePathSelection,filePathPublication;
    private bool filePathRefreshing,filePathClearing;
    private sealed class PathEntry(Guid id,string label):System.ComponentModel.INotifyPropertyChanged
    {
        public Guid Id{get;}=id;private string text=label;public string Label=>text;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        internal void Clear(){text="";try{PropertyChanged?.Invoke(this,new(nameof(Label)));}catch{}}
    }
    private PathEntry[]? pathEntries;
    public Button ConnectPathButton{get;}=new(){Content="파일 경로만 연결",Padding=new(6,3,6,3),Margin=new(0,4,6,0)};
    public Button DetachPathButton{get;}=new(){Content="선택 경로 분리",Padding=new(6,3,6,3),IsEnabled=false};
    public Button OpenPathButton{get;}=new(){Content="선택 원본 외부 열기",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
    public ListBox PathLinksList{get;}=new(){MaxHeight=100,MinHeight=24,DisplayMemberPath="Label",Margin=new(0,4,0,0)};
    private void AddPathControls(StackPanel content)
    {
        var buttons=new WrapPanel();buttons.Children.Add(ConnectPathButton);buttons.Children.Add(DetachPathButton);buttons.Children.Add(OpenPathButton);content.Children.Add(buttons);content.Children.Add(new TextBlock{Text="경로만 암호 저장합니다. 외부 원본은 복사·암호화하지 않습니다. 메모당 16개이며 다른 기기의 경로는 열지 않습니다.",TextWrapping=TextWrapping.Wrap});content.Children.Add(PathLinksList);
        ConnectPathButton.Click+=ConnectPathClicked;DetachPathButton.Click+=DetachPathClicked;OpenPathButton.Click+=OpenPathClicked;PathLinksList.SelectionChanged+=PathSelectionChanged;
    }
    private void PathSelectionChanged(object sender,SelectionChangedEventArgs e)
    {filePathSelection++;UpdatePathButtons();}
    private void UpdatePathButtons()
    {
        bool enabled=Current()&&!busy&&!filePathClearing&&filePathProfile!=Guid.Empty;
        ConnectPathButton.IsEnabled=enabled;
        if(!Current()){ClearPathLabels();return;}
        DetachPathButton.IsEnabled=enabled&&PathLinksList.SelectedItem is PathEntry;
        if(!Current()){ClearPathLabels();return;}
        OpenPathButton.IsEnabled=enabled&&SelectedPath() is{ } link&&link.UiDeviceId==filePathProfile;
        if(!Current())ClearPathLabels();
    }
    private StoredFilePathLink? SelectedPath()=>Current()&&PathLinksList.SelectedItem is PathEntry entry?note!.Metadata.FilePathLinks.SingleOrDefault(l=>l.Id==entry.Id):null;
    private void RefreshPathLinks()
    {
        if(filePathRefreshing||filePathClearing)return;
        filePathRefreshing=true;long publication=++filePathPublication;PathEntry[]? entries=null;
        try
        {
            if(!Current()){ClearPathLabels();return;}var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;Guid? selected=(PathLinksList.SelectedItem as PathEntry)?.Id;
            bool Allowed()=>Same(active,source,version)&&active.AttachmentPreviewEpoch==epoch&&filePathPublication==publication&&ReferenceEquals(pathEntries,entries);
            entries=source.Metadata.FilePathLinks.Select(l=>new PathEntry(l.Id,$"{l.Name} · {l.Path}"+(l.UiDeviceId==filePathProfile?"":" · 다른 기기: 열기 비활성"))).ToArray();
            var previous=pathEntries;pathEntries=entries;if(previous is not null)foreach(var item in previous)item.Clear();
            if(!Allowed()){ClearPathLabels();return;}PathLinksList.ItemsSource=entries;if(!Allowed()){ClearPathLabels();return;}
            PathLinksList.SelectedItem=entries.FirstOrDefault(l=>l.Id==selected);if(!Allowed()){ClearPathLabels();return;}
            UpdatePathButtons();if(!Allowed()){ClearPathLabels();return;}PathLinksList.Visibility=Visibility.Visible;if(!Allowed())ClearPathLabels();
        }
        catch{ClearPathLabels();}
        finally{if(entries is not null&&!ReferenceEquals(pathEntries,entries))foreach(var item in entries)item.Clear();filePathRefreshing=false;}
    }
    private void ClearPathLabels()
    {
        filePathSelection++;filePathPublication++;if(filePathClearing)return;filePathClearing=true;var previous=pathEntries;pathEntries=null;
        try
        {
            try{PathLinksList.Visibility=Visibility.Collapsed;}catch{}if(previous is not null)foreach(var item in previous)item.Clear();
            try{PathLinksList.SelectedItem=null;}catch{}try{PathLinksList.ItemsSource=null;}catch{}
            try{ConnectPathButton.IsEnabled=false;}catch{}try{DetachPathButton.IsEnabled=false;}catch{}try{OpenPathButton.IsEnabled=false;}catch{}
        }
        finally{filePathClearing=false;}
    }
    private async void ConnectPathClicked(object sender,RoutedEventArgs e)
    {
        await ConnectFilePathAsync(()=>
        {
            var owner=Window.GetWindow(this);if(owner is null)return null;var picker=new OpenFileDialog{Filter="경로만 연결할 로컬 원본 파일|*.*",Multiselect=false,CheckFileExists=true};return picker.ShowDialog(owner)==true?picker.FileName:null;
        },link=>
        {
            var owner=Window.GetWindow(this);return owner is not null&&MessageBox.Show(owner,$"원본: {link.Path}\n\n이 경로만 암호 저장합니다. 파일 내용은 복사·암호화하지 않으며 원본 변경·삭제는 메모앱과 독립적입니다. 다른 기기에서는 이 경로를 열지 않습니다. 연결할까요?","파일 경로 연결",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        });
    }
    public async Task<bool> ConnectFilePathAsync(Func<string?> choose,Func<StoredFilePathLink,bool> confirm,Func<string,Guid,StoredFilePathLink>? read=null)
    {
        Dispatcher.VerifyAccess();if(!Current()||busy||filePathRefreshing||filePathClearing||filePathProfile==Guid.Empty)return false;var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;var token=cancellation.Token;busy=true;
        bool Allowed()=>Same(active,source,version)&&active.AttachmentPreviewEpoch==epoch&&!token.IsCancellationRequested;
        StoredFilePathLink? link=null;
        try
        {
            ConnectPathButton.IsEnabled=false;if(!Allowed())return false;string? path=choose();if(path is null||!Allowed())return false;
            Guid profile=filePathProfile;link=await ReadFilePathAsync(path,profile,read??FilePathLink.Create,token);if(!Allowed()||!confirm(link)||!Allowed())return false;
            var preparation=active.PrepareAttachments(epoch);epoch=preparation.PreviewEpoch;if(!await preparation.Completion||!Allowed())return false;
            active.Workspace.AddFilePathLink(source,link);if(!Current()||!ReferenceEquals(session,active))return false;
            bool saved=await active.SaveAsync();if(!Current()||!ReferenceEquals(session,active))return false;
            Report(saved&&!active.IsDirty?"경로를 암호 저장했습니다. 외부 원본은 암호화하지 않았습니다.":"경로 연결을 반영했지만 저장 완료를 확인하지 못했습니다. 관리창 저장 상태를 확인하세요.");return saved;
        }
        catch(OperationCanceledException){return false;}
        catch{try{if(Current()){bool applied=link is not null&&source.Metadata.FilePathLinks.Any(l=>l.Id==link.Id);Report(applied?"경로 연결을 반영했지만 저장 완료를 확인하지 못했습니다. 저장·복구 상태를 확인하세요.":"경로를 연결하지 못했습니다. 로컬 일반 파일·기기·16개/이력/저장 한도를 확인하세요. 원본은 보존했습니다.");}}catch{}return false;}
        finally{busy=false;if(!IsDisposed)try{Refresh();}catch{}}
    }
    private static Task<StoredFilePathLink> ReadFilePathAsync(string path,Guid profile,Func<string,Guid,StoredFilePathLink> read,CancellationToken token)=>Task.Run(()=>{token.ThrowIfCancellationRequested();var link=read(path,profile);token.ThrowIfCancellationRequested();return link;},token);
    private void DetachPathClicked(object sender,RoutedEventArgs e)=>DetachSelectedPath();
    public bool DetachSelectedPath()
    {
        Dispatcher.VerifyAccess();if(!Current()||busy||filePathRefreshing||filePathClearing||SelectedPath() is not{ } link)return false;var active=session!;var source=note!;
        try{active.Workspace.DetachFilePathLink(source,link.Id);Report("경로 연결을 분리했습니다. 외부 파일은 삭제하지 않았고 암호 이력은 보존합니다. 저장 상태를 확인하세요.");return true;}
        catch{try{Report("경로 분리 저장·이력 상태를 확인하세요. 외부 파일은 삭제하지 않았습니다.");}catch{}return false;}
    }
    private void OpenPathClicked(object sender,RoutedEventArgs e)
    {
        OpenSelectedPath(link=>
        {
            var owner=Window.GetWindow(this);return owner is not null&&MessageBox.Show(owner,$"원본: {link.Path}\n\n현재 외부 원본을 기본 앱으로 엽니다. 연결 당시와 내용이 다를 수 있고, 외부 앱은 명령·매크로를 실행하거나 네트워크에 접근할 수 있습니다. 원본과 외부 앱 캐시는 메모앱 잠금 후에도 남습니다. 계속할까요?","원본 외부 열기",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        },path=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="open"}));
    }
    public bool OpenSelectedPath(Func<StoredFilePathLink,bool> confirm,Action<string> launch)
    {
        Dispatcher.VerifyAccess();if(!Current()||busy||filePathRefreshing||filePathClearing||SelectedPath() is not{ } link||link.UiDeviceId!=filePathProfile||filePathProfile==Guid.Empty)return false;
        var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,selection=filePathSelection;busy=true;
        bool Allowed()=>Same(active,source,version)&&active.AttachmentPreviewEpoch==epoch&&filePathSelection==selection&&SelectedPath()?.Id==link.Id;
        try
        {
            OpenPathButton.IsEnabled=false;if(!Allowed())return false;_=FilePathLink.ResolveForOpen(link,filePathProfile);if(!Allowed()||!confirm(link)||!Allowed())return false;
            string path=FilePathLink.ResolveForOpen(link,filePathProfile);if(!Allowed())return false;
            Report("외부 원본을 엽니다. 원본과 외부 앱 캐시는 메모앱 잠금 후에도 남습니다.");if(!Allowed())return false;
            launch(path);return Allowed(); // An external launch cannot be revoked; hostile path replacement is not prevented.
        }
        catch{try{Report("원본을 열지 못했습니다. 경로·기기·파일 접근 상태를 확인하세요. 외부 원본은 바꾸지 않았습니다.");}catch{}return false;}
        finally{busy=false;if(!IsDisposed)try{RefreshPathLinks();}catch{}}
    }
    private void DisposePathControls()
    {
        ConnectPathButton.Click-=ConnectPathClicked;DetachPathButton.Click-=DetachPathClicked;OpenPathButton.Click-=OpenPathClicked;PathLinksList.SelectionChanged-=PathSelectionChanged;ClearPathLabels();
    }
}

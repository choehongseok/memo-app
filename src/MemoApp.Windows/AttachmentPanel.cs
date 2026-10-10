using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using MemoApp.Core.Transfer;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;
namespace MemoApp.Windows;

// Opaque originals, explicit bounded PNG display and consented external plaintext copies.
public sealed partial class AttachmentPanel : UserControl,IDisposable,IImageDisplayHost
{
    private SaveCoordinator? session;
    private NoteDraft? note;
    private Func<bool>? current;
    private Action<string>? notice;
    private readonly CancellationTokenSource cancellation=new();
    private readonly Button add=new(){Content="파일 첨부",Padding=new(6,3,6,3),Margin=new(0,0,6,0)};
    private readonly Button detach=new(){Content="선택 첨부 분리",Padding=new(6,3,6,3),IsEnabled=false};
    private bool busy,refreshing,refreshPending;
    private readonly ImagePreviewBackend previewBackend;
    private CancellationTokenSource? previewCancellation;
    private long previewGeneration,observedPreviewEpoch;
    private readonly Button preview=new(){Content="선택 이미지 미리보기",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
    public Image PreviewImage{get;}=new(){MaxWidth=1024,MaxHeight=240,Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};
    private sealed record Entry(Guid Id,string Label,string Mime);
    public ListBox FilesList{get;}=new(){MaxHeight=110,MinHeight=24,DisplayMemberPath="Label",Margin=new(0,4,0,0)};
    public bool IsDisposed{get;private set;}
    public AttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice)
        :this(session,note,current,notice,new ImagePreviewBackend(),Guid.Empty){}
    public AttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice,Guid uiDeviceId)
        :this(session,note,current,notice,new ImagePreviewBackend(),uiDeviceId){}
    internal AttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice,ImagePreviewBackend backend)
        :this(session,note,current,notice,backend,Guid.Empty){}
    internal AttachmentPanel(SaveCoordinator session,NoteDraft note,Func<bool> current,Action<string> notice,ImagePreviewBackend backend,Guid uiDeviceId)
    {
        Dispatcher.VerifyAccess();previewBackend=backend;filePathProfile=uiDeviceId;
        this.session=session;this.note=note;this.current=current;this.notice=notice;observedPreviewEpoch=session.AttachmentPreviewEpoch;
        var content=new StackPanel();var buttons=new WrapPanel();buttons.Children.Add(add);buttons.Children.Add(detach);buttons.Children.Add(preview);buttons.Children.Add(pastePng);buttons.Children.Add(OpenButton);buttons.Children.Add(inlineImage);content.Children.Add(buttons);
        content.Children.Add(new TextBlock{Text="원본 파일 4 MiB · 메모당 16개 · 보존 파일 합계 8 MiB · 자동 실행 없음",TextWrapping=TextWrapping.Wrap});content.Children.Add(FilesList);content.Children.Add(new TextBlock{Text="이미지 파일 한 개를 여기에 끌어놓으면 원본을 암호 첨부로 복사합니다. 미리보기는 선택 후 버튼으로 표시하며 제한된 PNG와 고정 색 공간·픽셀 밀도 정보만 지원합니다. 표시 색상·비율은 원본 메타데이터에 맞춰 보정하지 않습니다.",TextWrapping=TextWrapping.Wrap});content.Children.Add(new TextBlock{Text="첨부 영역에 초점을 둔 Ctrl+V/PNG 붙이기는 자동 변환 없는 제한 PNG만 받습니다. Bitmap/DIB·일반 스크린샷은 지원하지 않을 수 있습니다. OS 클립보드 원본은 앱 잠금 후에도 남습니다.",TextWrapping=TextWrapping.Wrap});content.Children.Add(PreviewImage);AddOcrControls(content);AddPathControls(content);Content=content;
        AllowDrop=true;PreviewDragOver+=ImageDragOver;PreviewDrop+=ImageDrop;
        add.Click+=AddClicked;pastePng.Click+=PastePngClicked;PreviewKeyDown+=ClipboardKeyDown;detach.Click+=DetachClicked;preview.Click+=PreviewClicked;inlineImage.Click+=InlineImageClicked;OpenButton.Click+=OpenClicked;FilesList.MouseDoubleClick+=AttachmentDoubleClick;FilesList.SelectionChanged+=SelectionChanged;Dispatcher.ShutdownStarted+=DispatcherClosing;
        session.Workspace.Changed+=Refresh;session.Conceal+=Dispose;session.Changed+=PreviewStateChanged;CompositionTarget.Rendering+=PreviewRendering;note.PropertyChanged+=NoteChanged;Refresh();
    }
    private bool Current()=>!IsDisposed&&session is {IsLocked:false} active&&note is {IsClosed:false,IsDeleted:false} source&&active.Workspace.Notes.Contains(source)&&current?.Invoke()==true;
    private bool Same(SaveCoordinator active,NoteDraft source,long version)=>Current()&&ReferenceEquals(session,active)&&ReferenceEquals(note,source)&&source.EditVersion==version;
    private void Report(string message){if(Current())notice?.Invoke(message);}
    private void NoteChanged(object? sender,PropertyChangedEventArgs e)
    {InvalidatePreview();if(e.PropertyName is nameof(NoteDraft.IsClosed) or nameof(NoteDraft.IsDeleted)){if(!Current())Dispose();}}
    private void SelectionChanged(object sender,SelectionChangedEventArgs e)
    {inlineSelectionGeneration++;InvalidatePreview();invalidateInlineImage?.Invoke();detach.IsEnabled=preview.IsEnabled=OpenButton.IsEnabled=Current()&&!busy&&FilesList.SelectedItem is Entry;RefreshOcr();RefreshInlineImage();}
    private void PreviewRendering(object? sender,EventArgs e)=>PreviewStateChanged();
    private void PreviewStateChanged()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;if(!Current()){Dispose();return;}if(session is not { } active)return;
        if(observedPreviewEpoch!=active.AttachmentPreviewEpoch){observedPreviewEpoch=active.AttachmentPreviewEpoch;InvalidatePreview();ClearPathLabels();RefreshPathLinks();}
    }
    private void DispatcherClosing(object? sender,EventArgs e)=>Dispose();
    private async void PreviewClicked(object sender,RoutedEventArgs e)=>await PreviewSelectedAsync();
    void IImageDisplayHost.InvalidateImageDisplay()=>InvalidatePreview();
    internal void InvalidatePreview()
    {
        Dispatcher.VerifyAccess();previewGeneration++;ClearOcr();var cancel=previewCancellation;previewCancellation=null;
        ImagePreviewAdmission.Forget(this);
        try{cancel?.Cancel();}catch{}
        try{cancel?.Dispose();}catch{}
        try{PreviewImage.Visibility=Visibility.Collapsed;}catch{}
        try{PreviewImage.Source=null;}catch{}
    }
    public Task<bool> PreviewSelectedAsync()
    {
        Dispatcher.VerifyAccess();
        if(!Current()||busy||FilesList.SelectedItem is not Entry selected)return Task.FromResult(false);
        if(!ImagePreviewAdmission.TryEnter(Dispatcher)){Report("다른 이미지 처리 중입니다. 처리 후 다시 미리보기를 눌러 주세요.");return Task.FromResult(false);}
        AttachmentReadLease? lease=null;CancellationTokenSource? cancel=null;
        try
        {
            ImagePreviewAdmission.ClearDisplayed();InvalidatePreview();
            var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,generation=previewGeneration;observedPreviewEpoch=epoch;
            cancel=new();previewCancellation=cancel;var token=cancel.Token;
            bool Allowed()=>Same(active,source,version)&&!token.IsCancellationRequested&&generation==previewGeneration&&FilesList.SelectedItem is Entry item&&item.Id==selected.Id&&active.IsAttachmentPreviewCurrent(source,selected.Id,version,epoch);
            if(!Allowed())throw new OperationCanceledException();
            lease=active.CreateAttachmentReadLease(source,selected.Id,version);
            if(!Allowed())throw new OperationCanceledException();
            // Worker receives only the backend, lease and token. The authority closure stays in the UI callback.
            return RunPreviewAsync(previewBackend,lease,cancel,Dispatcher,Allowed);
        }
        catch
        {
            lease?.Dispose();cancel?.Dispose();if(ReferenceEquals(previewCancellation,cancel))previewCancellation=null;
            ImagePreviewAdmission.Exit();return Task.FromResult(false);
        }
    }
    private static Task<OwnedBgraRaster> DecodeAsync(ImagePreviewBackend backend,AttachmentReadLease lease,CancellationToken token)=>Task.Run(()=>backend.Decode(lease,token));
    private async Task<bool> RunPreviewAsync(ImagePreviewBackend backend,AttachmentReadLease lease,CancellationTokenSource cancel,Dispatcher context,Func<bool> allowed)
    {
        OwnedBgraRaster? raster=null;var token=cancel.Token;
        try
        {
            raster=await DecodeAsync(backend,lease,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();var detached=raster;
            return await context.InvokeAsync(()=>
            {
                if(!allowed())return false;
                var bitmap=backend.Create(detached);
                if(!allowed())return false;
                ImagePreviewAdmission.PublishHost(this);
                PreviewImage.Width=bitmap.PixelWidth;PreviewImage.Height=bitmap.PixelHeight;
                PreviewImage.Source=bitmap;
                if(!allowed()){InvalidatePreview();return false;}
                PreviewImage.Visibility=Visibility.Visible;
                if(!allowed()){InvalidatePreview();return false;}
                return true;
            },DispatcherPriority.Background,token).Task.ConfigureAwait(false);
        }
        catch
        {
            if(!context.HasShutdownStarted)
                try{await context.InvokeAsync(()=>{InvalidatePreview();Report("이 이미지를 표시하지 못했습니다. 제한된 PNG 형식·크기를 확인하세요. 원본은 보존했습니다.");},DispatcherPriority.Background).Task.ConfigureAwait(false);}catch{}
            return false;
        }
        finally{raster?.Dispose();lease.Dispose();cancel.Dispose();ImagePreviewAdmission.Exit();}
    }
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
    private void ImageDragOver(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if(!Current()||busy||(e.AllowedEffects&DragDropEffects.Copy)==0)return;
        var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;
        try{if(e.Data.GetDataPresent(DataFormats.FileDrop,false)&&Same(active,source,version)&&active.AttachmentPreviewEpoch==epoch)e.Effects=DragDropEffects.Copy;}catch{}
    }
    private async void ImageDrop(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if((e.AllowedEffects&DragDropEffects.Copy)==0)return;
        var request=ImportDroppedImageAsync(e.Data);
        // OLE receives the accepted copy request before asynchronous read/save; this is not a save receipt.
        if(!request.IsCompletedSuccessfully||request.Result)e.Effects=DragDropEffects.Copy;
        await request;
    }
    public Task<bool> ImportDroppedImageAsync(IDataObject data)
    {
        Dispatcher.VerifyAccess();if(!Current()||busy)return Task.FromResult(false);
        var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;
        bool Allowed()=>Same(active,source,version)&&active.AttachmentPreviewEpoch==epoch;
        return ImportFileAsync(()=>
        {
            if(!Allowed()||!data.GetDataPresent(DataFormats.FileDrop,false)||!Allowed())return null;
            object value=data.GetData(DataFormats.FileDrop,false);
            if(!Allowed()||value is not string[] {Length:1} paths)return null;
            string? path=paths[0];
            if(string.IsNullOrEmpty(path)||!SupportedImageExtension(System.IO.Path.GetExtension(path))||!Allowed())return null;
            return path;
        });
    }
    private static bool SupportedImageExtension(string extension)=>extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff";
    public async Task<bool> ImportFileAsync(Func<string?> choose)
    {
        if(!Current()||busy)return false;var active=session!;var sourceNote=note!;long version=sourceNote.EditVersion;var token=cancellation.Token;busy=true;Refresh();
        try
        {
            if(!Same(active,sourceNote,version))return false;
            string? path=choose();if(path is null||!Same(active,sourceNote,version))return false;
            token.ThrowIfCancellationRequested();
            using(var input=await Task.Run(()=>AttachmentSource.Read(path,token),token))
            {
                if(!Same(active,sourceNote,version)||token.IsCancellationRequested)return false;
                if(!await active.PrepareAttachmentsAsync()||!Same(active,sourceNote,version)||token.IsCancellationRequested)return false;
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
            var entries=active.Workspace.DescribeAttachments(source).Select(item=>new Entry(item.Id,$"{item.Name} · {item.Length:N0} bytes",item.Mime)).ToArray();
            if(!Same(active,source,version))return;FilesList.ItemsSource=entries;
            if(!Same(active,source,version)){ClearLabels();return;}
            FilesList.SelectedItem=entries.FirstOrDefault(item=>item.Id==selected);
            if(!Same(active,source,version)){ClearLabels();return;}
            add.IsEnabled=pastePng.IsEnabled=!busy;detach.IsEnabled=preview.IsEnabled=OpenButton.IsEnabled=!busy&&FilesList.SelectedItem is Entry;RefreshPathLinks();RefreshOcr();RefreshInlineImage();
        }
        catch{if(!IsDisposed)ClearLabels();}
        finally
        {
            refreshing=false;if(refreshPending&&!IsDisposed){refreshPending=false;Dispatcher.BeginInvoke(new Action(()=>{if(!IsDisposed)Refresh();}),DispatcherPriority.Background);}
        }
    }
    private void ClearLabels()
    {
        ClearPathLabels();
        try{FilesList.SelectedItem=null;}catch{}
        try{FilesList.ItemsSource=null;}catch{}
        try{add.IsEnabled=false;}catch{}
        try{pastePng.IsEnabled=false;}catch{}
        try{detach.IsEnabled=false;}catch{}
        try{preview.IsEnabled=false;}catch{}
        try{OpenButton.IsEnabled=false;}catch{}
        try{inlineImage.IsEnabled=false;}catch{}
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess();if(IsDisposed)return;IsDisposed=true;InvalidatePreview();var active=session;var source=note;session=null;note=null;current=null;notice=null;refreshPending=false;
        if(active is not null){active.Workspace.Changed-=Refresh;active.Conceal-=Dispose;active.Changed-=PreviewStateChanged;}if(source is not null)source.PropertyChanged-=NoteChanged;
        // Conceal this host before native collection callbacks; parents also conceal independently.
        try{Visibility=Visibility.Collapsed;}catch{}
        try{Content=null;}catch{}
        try{cancellation.Cancel();}catch{}DisposeOcr();DisposePathControls();ClearLabels();add.Click-=AddClicked;pastePng.Click-=PastePngClicked;PreviewKeyDown-=ClipboardKeyDown;detach.Click-=DetachClicked;OpenButton.Click-=OpenClicked;FilesList.MouseDoubleClick-=AttachmentDoubleClick;FilesList.SelectionChanged-=SelectionChanged;preview.Click-=PreviewClicked;inlineImage.Click-=InlineImageClicked;insertInlineImage=null;canInsertInlineImage=null;invalidateInlineImage=null;PreviewDragOver-=ImageDragOver;PreviewDrop-=ImageDrop;Dispatcher.ShutdownStarted-=DispatcherClosing;CompositionTarget.Rendering-=PreviewRendering;cancellation.Dispose();
    }
}

using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using Microsoft.Win32;

namespace MemoApp.Windows;

public partial class MainWindow
{
    private bool backupMergeBusy;
    private readonly HashSet<BackupMergeWindow> backupMergeWindows=[];
    private readonly Dictionary<BackupMergeWindow,BackupMergeOperation> backupMergeOperations=[];
    private void ClearBackupMergeViews()
    {foreach(var operation in backupMergeOperations.Values.ToArray())try{operation.Dispose();}catch{}foreach(var viewer in backupMergeWindows.ToArray())try{viewer.Dispose();}catch{}backupMergeOperations.Clear();backupMergeWindows.Clear();}
    private async void BackupMerge_Click(object sender,RoutedEventArgs e)=>await ShowBackupMergeAsync(()=>
    {var picker=new OpenFileDialog{Filter="암호 메모 백업|*.vault",CheckFileExists=true,Multiselect=false};return picker.ShowDialog(this)==true?picker.FileName:null;});
    private async void PendingBackupBranches_Click(object sender,RoutedEventArgs e)=>await ShowPendingBackupBranchesAsync();
    internal async Task<bool> ShowBackupMergeAsync(Func<string?> choose)
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||backupMergeBusy||CaptureBatch(false) is not{ } request)return false;
        var active=request.Session;BackupMergeOperation? operation=null;byte[]? cipher=null;bool published=false;backupMergeBusy=true;
        try
        {
            ClearBackupMergeViews();operation=new(this,request);UpdateSelectedActions();if(!operation.Current())return false;
            string? path=choose();if(path is null||!operation.Current())return false;
            cipher=await ReadBackupCipherAsync(path,operation.Token);if(!operation.Current())return false;
            // Core owns the bounded ciphertext/selection before its checkpoint callback or await.
            operation.BeginCheckpoint();BackupMergePreview preview;
            try{preview=await active.PreviewEncryptedBackupMergeAsync(cipher,request.Notes.Select(n=>n.Id).ToArray(),path,operation.PreviewEpoch,operation.View);}
            finally{operation.EndCheckpoint();CryptographicOperations.ZeroMemory(cipher);cipher=null;}
            if(!operation.Current()||active.IsDirty)return false;operation.Settle(preview.Token,path);
            var viewer=AttachBackupMergeWindow(operation,true);if(viewer is null)return false;
            if(!viewer.Publish(preview.BranchDetails,preview.SelectedIds.Length,preview.ImportedRevisionCount,true)||!operation.Current())return false;
            // Showing all native fields succeeds before this exact token becomes confirmable.
            operation.View.MarkDisplayed(preview.Token);if(!operation.Current())return false;
            viewer.ConfigurePreserve(()=>ConfirmBackupMergeAsync(viewer,()=>MessageBox.Show(this,"현재 메모의 내용과 현재 이력 끝은 유지하고, 선택한 백업의 같은 메모 ID 이력을 암호 저장합니다. 미해결 분기는 비교 목록에 남습니다. 삭제 메모를 되살리거나 백업을 현재 내용으로 승격하지 않습니다. 적용 전 현재 암호문 사본을 보존합니다. 계속할까요?","백업 이력 보존",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes));
            if(!operation.Current())return false;Notice.Text="현재 메모와 백업 분기를 비교합니다. 이력 보존은 별도 확인 후 적용합니다.";if(!operation.Current())return false;
            published=true;return true;
        }
        catch(OperationCanceledException){return false;}
        catch{if(operation?.Current()==true)try{Notice.Text="백업 분기 비교 실패 — 같은 저장소의 인증된 로컬 암호 백업, 선택한 모든 현재 메모의 존재, 같은 메모 ID·작성 시각·범위와 첨부 키·전체 한도를 확인하세요. 현재 메모는 유지합니다. 미저장 수정의 선행 저장은 별도 완료되었을 수 있습니다.";}catch{}return false;}
        finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);if(!published)operation?.Dispose();backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
    }
    internal async Task<bool> ConfirmBackupMergeAsync(BackupMergeWindow viewer,Func<bool> confirm,IAtomicVaultFiles? recoveryFiles=null)
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||backupMergeBusy||!backupMergeOperations.TryGetValue(viewer,out var operation)||operation.PreviewToken is not{ } preview||!operation.Current()||viewer.IsRevoked||!viewer.IsVisible)return false;
        byte[]? cipher=null;BackupMergeResult? result=null;bool accepted=false;backupMergeBusy=true;
        try
        {
            UpdateSelectedActions();if(!operation.Current()||!confirm()||!operation.Current())return false;accepted=true;
            var confirmed=operation.Session.ConfirmBackupMerge(preview);if(!operation.Current())return false;
            cipher=await ReadBackupCipherAsync(operation.SourcePath!,operation.Token);if(!operation.Current())return false;
            operation.BeginApply();
            try{result=recoveryFiles is null?await operation.Session.MergeEncryptedBackupAsync(confirmed,cipher):await operation.Session.StartBackupMerge(confirmed,cipher,recoveryFiles);}
            finally{operation.EndApply();CryptographicOperations.ZeroMemory(cipher);cipher=null;}
            // Apply may legitimately revoke the consumed view. Result, not note-ID changes, identifies publication.
            if(windowClosed||concealing||!SameFileSession(operation.Session,operation.UiEpoch))return false;
            Notice.Text=result.Disposition switch
            {
                BackupMergeDisposition.AppliedAndSaved=>$"현재 메모를 유지하고 백업 이력 {result.ImportedRevisionCount}개를 암호 저장했습니다. 미해결 분기는 ‘보존된 백업 분기 비교’에서 확인하세요.",
                BackupMergeDisposition.AppliedDirty=>"백업 이력 전체를 반영했지만 암호 저장을 완료하지 못했습니다. 전체 변경을 유지합니다. 저장 상태를 확인하세요.",
                BackupMergeDisposition.NoChange=>"선택한 백업 이력은 이미 보존되어 있습니다. 현재 메모를 유지했습니다.",
                _=>"백업 이력을 적용하지 못했습니다. 원본 교체·선택·수정·잠금·보기 변경 또는 암호문 보존 실패를 확인하세요. 현재 자료와 원본 백업을 유지했습니다."
            };
            return !windowClosed&&!concealing&&SameFileSession(operation.Session,operation.UiEpoch)&&result.Disposition is BackupMergeDisposition.AppliedAndSaved or BackupMergeDisposition.NoChange;
        }
        catch(OperationCanceledException){return false;}
        catch{if(!windowClosed&&!concealing&&SameFileSession(operation.Session,operation.UiEpoch))try{Notice.Text="백업 이력 보존을 완료하지 못했습니다. 현재 자료를 유지했습니다. 선행 암호문 사본과 저장 상태를 확인하세요.";}catch{}return false;}
        finally{if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);if(accepted||result is not null)operation.Dispose();backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
    }
    internal async Task<bool> ShowPendingBackupBranchesAsync()
    {
        Dispatcher.VerifyAccess();if(windowClosed||concealing||backupMergeBusy||CaptureBatch(false) is not{Notes.Length:1} request)return false;
        BackupMergeOperation? operation=null;bool published=false;backupMergeBusy=true;
        try
        {
            ClearBackupMergeViews();operation=new(this,request);UpdateSelectedActions();if(!operation.Current())return false;
            operation.BeginCheckpoint();bool saved;
            try{saved=!request.Session.IsDirty||await request.Session.SaveAsync();}finally{operation.EndCheckpoint();}
            if(!saved||request.Session.IsDirty||!operation.Current())return false;operation.Settle(null,null);
            var details=request.Session.Workspace.DescribePendingBackupBranches(request.Notes[0]);if(!operation.Current())return false;
            var viewer=AttachBackupMergeWindow(operation,false);if(viewer is null||!viewer.Publish(details,1,0,false)||!operation.Current())return false;
            published=true;return true;
        }
        catch(OperationCanceledException){return false;}
        catch{if(operation?.Current()==true)try{Notice.Text="보존된 백업 분기 비교 실패 — 현재 수정의 암호 저장과 현재 메모 상태를 확인하세요.";}catch{}return false;}
        finally{if(!published)operation?.Dispose();backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
    }
    private BackupMergeWindow? AttachBackupMergeWindow(BackupMergeOperation operation,bool mergePreview)
    {
        if(!operation.Current())return null;var viewer=new BackupMergeWindow(operation.Current,mergePreview);operation.Window=viewer;
        try
        {
            viewer.Owner=this;if(!operation.Current())return null;backupMergeWindows.Add(viewer);backupMergeOperations.Add(viewer,operation);
            operation.RegisterWindowClosed();return viewer;
        }
        catch{viewer.Dispose();throw;}
    }
    // One monotonic request/view capability; no keys, cipher buffers, or candidate graphs are stored here.
    private sealed class BackupMergeOperation:IDisposable
    {
        private readonly MainWindow owner;
        private readonly BatchRequest request;
        private readonly EditingWorkspace workspace;
        private readonly NoteDraft[] notes;
        private readonly long[] versions;
        private readonly CancellationTokenSource cancellation;
        private bool revoked,checkpoint,checkpointExpected,checkpointObserved,settled,applying,applyObserved;
        private EventHandler? viewerClosed;
        public SaveCoordinator Session=>request.Session;
        public long UiEpoch=>request.Epoch;
        public long PreviewEpoch{get;private set;}
        public CancellationToken Token{get;}
        public BackupMergeViewAuthority View{get;}
        public BackupMergePreviewToken? PreviewToken{get;private set;}
        public string? SourcePath{get;private set;}
        public BackupMergeWindow? Window{get;set;}
        public BackupMergeOperation(MainWindow owner,BatchRequest request)
        {
            this.owner=owner;this.request=request;workspace=request.Session.Workspace;notes=workspace.Notes.ToArray();versions=notes.Select(n=>n.EditVersion).ToArray();PreviewEpoch=Session.AttachmentPreviewEpoch;
            cancellation=CancellationTokenSource.CreateLinkedTokenSource(owner.fileOperations.Token);Token=cancellation.Token;
            try{View=Session.CreateBackupMergeView(Current,owner.Dispatcher.CheckAccess);}catch{cancellation.Dispose();throw;}
            owner.NotesList.SelectionChanged+=SelectionChanged;foreach(var note in notes)note.PropertyChanged+=SourceChanged;
            workspace.AttachmentReadInvalidating+=Invalidating;Session.Changed+=SessionChanged;Session.Conceal+=Dispose;owner.Closed+=WindowClosed;
        }
        private bool EpochCurrent()=>Session.AttachmentPreviewEpoch==PreviewEpoch||Session.AttachmentPreviewEpoch==PreviewEpoch+1&&
            (checkpoint&&checkpointExpected&&!checkpointObserved&&!workspace.IsMergePublicationActive||applying&&!applyObserved&&workspace.IsMergePublicationActive);
        // Core rechecks after each invalidation subscriber, including before this host's subscriber runs.
        // Admit its exact in-flight +1 only; the subscriber below still consumes that allowance once.
        public bool Current()=>!revoked&&!Token.IsCancellationRequested&&!owner.windowClosed&&!owner.concealing&&owner.CurrentBatch(request)&&EpochCurrent()&&(!settled||applying||!Session.IsDirty)&&workspace.Notes.Count==notes.Length&&notes.Where((n,i)=>n.IsClosed||n.EditVersion!=versions[i]||!workspace.Notes.Contains(n)).Any()==false&&owner.NotesList.SelectedItems.Count==request.Notes.Length&&request.Notes.All(n=>owner.NotesList.SelectedItems.Contains(n))&&(Window is null||!Window.IsRevoked);
        public void BeginCheckpoint(){checkpoint=true;checkpointExpected=Session.IsDirty;checkpointObserved=false;}
        public void EndCheckpoint(){checkpoint=false;}
        public void Settle(BackupMergePreviewToken? token,string? source){settled=true;PreviewToken=token;SourcePath=source;}
        public void RegisterWindowClosed(){viewerClosed=(_,_)=>Dispose();Window!.Closed+=viewerClosed;}
        public void BeginApply(){applying=true;applyObserved=false;}
        public void EndApply(){applying=false;}
        private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>Dispose();
        private void SourceChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(NoteDraft.EditVersion))Dispose();}
        private void WindowClosed(object? sender,EventArgs e)=>Dispose();
        private void SessionChanged(){if(!Current())Dispose();}
        private void Invalidating(NoteDraft? source)
        {
            // Exactly one own initial SaveAsync invalidation, before plaintext is displayed; Core checks generation/basis.
            if(!revoked&&source is null&&checkpoint&&checkpointExpected&&!checkpointObserved&&!workspace.IsMergePublicationActive&&Session.AttachmentPreviewEpoch==PreviewEpoch+1)
            {checkpointObserved=true;PreviewEpoch++;if(!Current())Dispose();return;}
            // Exactly one own Apply invalidation. Reentrant edits/AcceptPrepared cannot borrow this permit twice.
            if(!revoked&&source is null&&applying&&!applyObserved&&workspace.IsMergePublicationActive&&Session.AttachmentPreviewEpoch==PreviewEpoch+1)
            {applyObserved=true;PreviewEpoch++;if(!Current())Dispose();return;}
            Dispose();
        }
        public void Dispose()
        {
            if(revoked)return;revoked=true;
            owner.NotesList.SelectionChanged-=SelectionChanged;foreach(var note in notes)note.PropertyChanged-=SourceChanged;
            workspace.AttachmentReadInvalidating-=Invalidating;Session.Changed-=SessionChanged;Session.Conceal-=Dispose;owner.Closed-=WindowClosed;
            if(Window is{ } attached&&viewerClosed is{ } handler){attached.Closed-=handler;viewerClosed=null;}
            try{cancellation.Cancel();}catch{}finally{cancellation.Dispose();}
            try{View.Dispose();}finally{if(Window is{ } viewer){owner.backupMergeOperations.Remove(viewer);owner.backupMergeWindows.Remove(viewer);viewer.Dispose();}PreviewToken=null;SourcePath=null;}
        }
    }
}

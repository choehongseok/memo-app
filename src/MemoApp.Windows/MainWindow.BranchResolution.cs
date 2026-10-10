using System.ComponentModel;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

namespace MemoApp.Windows;

public partial class MainWindow
{
 private readonly Dictionary<BackupMergeWindow,BranchResolutionOperation> branchResolutionOperations=[];
 private void ClearBranchResolutionViews()
 {foreach(var operation in branchResolutionOperations.Values.ToArray())try{operation.Dispose();}catch{}branchResolutionOperations.Clear();}
 private async Task<bool> ShowBranchResolutionComparisonAsync()
 {
  Dispatcher.VerifyAccess();if(windowClosed||concealing||backupMergeBusy||CaptureBatch(false) is not{Notes.Length:1} request)return false;
  BranchResolutionOperation? operation=null;bool published=false;backupMergeBusy=true;
  try
  {
   ClearBackupMergeViews();operation=new(this,request);UpdateSelectedActions();if(!operation.Current())return false;
   operation.BeginCheckpoint();bool saved;
   try{saved=!request.Session.IsDirty||await request.Session.SaveAsync();}finally{operation.EndCheckpoint();}
   if(!saved||request.Session.IsDirty||!operation.Current())return false;operation.Settle();
   var details=request.Session.Workspace.DescribePendingBackupBranches(request.Notes[0]);if(!operation.Current())return false;
   var viewer=new BackupMergeWindow(operation.Current,false);operation.Window=viewer;
   viewer.Owner=this;if(!operation.Current())return false;backupMergeWindows.Add(viewer);branchResolutionOperations.Add(viewer,operation);operation.RegisterClosed();
   if(!viewer.Publish(details,1,0,false)||!operation.Current())return false;operation.MarkPresented();if(!operation.Current())return false;
   var host=operation;viewer.ConfigureResolution(host.InvalidateSelection,branch=>PreviewBranchResolutionSelectionAsync(host,branch),()=>ConfirmBranchResolutionAsync(viewer,()=>MessageBox.Show(this,"현재 내용을 그대로 유지하며 선택한 보존 분기를 확인 완료로 기록합니다. 두 수정본의 내용은 이력에 남고, 다른 미해결 분기는 유지합니다. 적용 전 현재 암호문을 보존합니다. 계속할까요?","현재 내용 유지 · 선택 분기 해결",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes));
   if(!operation.Current())return false;Notice.Text="보존된 분기를 직접 선택해 비교하세요. 현재 내용 유지는 별도 확인 후 두 수정본을 이력에 남깁니다.";if(!operation.Current())return false;
   published=true;return true;
  }
  catch(OperationCanceledException){return false;}
  catch{if(operation?.Current()==true)try{Notice.Text="보존 분기 비교 실패 — 현재 수정의 암호 저장과 메모 상태를 확인하세요. 미저장 수정의 선행 저장은 별도 완료되었을 수 있습니다.";}catch{}return false;}
  finally{if(!published)operation?.Dispose();backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
 }
 private async Task<bool> PreviewBranchResolutionSelectionAsync(BranchResolutionOperation operation,BackupMergeBranchComparison? branch)
 {
  Dispatcher.VerifyAccess();if(branch is null||backupMergeBusy||!operation.Current()||operation.Window is not{ } viewer||branch.NoteId!=operation.Note.Id)return false;
  backupMergeBusy=true;BranchResolutionViewAuthority? view=null;bool displayed=false;
  try
  {
   view=operation.BeginSelection(branch.Incoming.RevisionId);if(!operation.SelectedCurrent(view))return false;
   var preview=await operation.Session.PreviewBranchResolutionAsync(operation.Note.Id,branch.Incoming.RevisionId,operation.PreviewEpoch,view);
   if(!operation.SelectedCurrent(view)||preview.NoteId!=operation.Note.Id||preview.SelectedTipRevisionId!=branch.Incoming.RevisionId)return false;
   if(!viewer.PublishResolutionComparison(preview.Comparison)||!operation.SelectedCurrent(view))return false;
   view.MarkDisplayed(preview.Token);if(!operation.SelectedCurrent(view))return false;
   operation.Preview=preview.Token;
   if(!viewer.SetResolutionReady(true)||!operation.SelectedCurrent(view))return false;
   displayed=true;return true;
  }
  catch(OperationCanceledException){return false;}
  catch{if(operation.Current())try{Notice.Text="선택한 분기를 확인하지 못했습니다. 현재의 살아 있는 최대 분기와 저장·선택 상태를 확인하세요. 현재 내용을 유지합니다.";}catch{}return false;}
  finally{if(!displayed&&view is not null)operation.RetireSelection(view);backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
 }
 internal async Task<bool> ConfirmBranchResolutionAsync(BackupMergeWindow viewer,Func<bool> confirm,IAtomicVaultFiles? recoveryFiles=null)
 {
  Dispatcher.VerifyAccess();if(windowClosed||concealing||backupMergeBusy||!branchResolutionOperations.TryGetValue(viewer,out var operation)||operation.Preview is not{ } preview||operation.View is not{ } view||!operation.SelectedCurrent(view)||viewer.IsRevoked||!viewer.IsVisible)return false;
  bool accepted=false;BranchResolutionResult? result=null;backupMergeBusy=true;
  try
  {
   UpdateSelectedActions();if(!operation.SelectedCurrent(view)||!confirm()||!operation.SelectedCurrent(view)||!ReferenceEquals(operation.Preview,preview))return false;
   accepted=true;var confirmed=operation.Session.ConfirmBranchResolution(preview);if(!operation.SelectedCurrent(view))return false;
   operation.BeginApply();try{result=recoveryFiles is null?await operation.Session.ResolvePendingBranchAsync(confirmed):await operation.Session.StartBranchResolution(confirmed,recoveryFiles);}finally{operation.EndApply();}
   // Publication changes version/epoch and can revoke the consumed host. The authoritative receipt survives it.
   if(windowClosed||concealing||!SameFileSession(operation.Session,operation.UiEpoch))return false;
   Notice.Text=result.Disposition switch
   {
    BranchResolutionDisposition.AppliedAndSaved=>"현재 내용을 유지하고 선택 분기를 암호 저장으로 해결했습니다. 두 수정본은 이력에 남고 다른 미해결 분기는 유지합니다.",
    BranchResolutionDisposition.AppliedDirty=>"현재 내용 유지와 선택 분기 해결 전체를 반영했지만 암호 저장을 완료하지 못했습니다. 전체 변경을 유지합니다. 저장 상태를 확인하세요.",
    _=>"선택 분기를 해결하지 못했습니다. 수정·선택·잠금·보기 변경, 현재 암호문 교체와 보존 실패를 확인하세요. 현재 내용과 분기 이력을 유지합니다."
   };
   return !windowClosed&&!concealing&&SameFileSession(operation.Session,operation.UiEpoch)&&result.Disposition==BranchResolutionDisposition.AppliedAndSaved;
  }
  catch(OperationCanceledException){return false;}
  catch{if(!windowClosed&&!concealing&&SameFileSession(operation.Session,operation.UiEpoch))try{Notice.Text="선택 분기 해결 완료를 확인하지 못했습니다. 현재 자료와 선행 암호문 사본을 유지합니다. 저장 상태를 확인하세요.";}catch{}return false;}
  finally{if(accepted||result is not null)operation.Dispose();backupMergeBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}}
 }
 private sealed class BranchResolutionOperation:IDisposable
 {
  private readonly MainWindow owner;private readonly BatchRequest request;private readonly EditingWorkspace workspace;private readonly NoteDraft[] notes;private readonly long[] versions;
  private readonly CancellationTokenSource cancellation;private readonly CancellationToken token;
  private bool revoked,checkpoint,checkpointExpected,checkpointObserved,settled,presented,applying,applyObserved;private long selectionGeneration;private Guid selectedTip;private EventHandler? closed;
  internal SaveCoordinator Session=>request.Session;internal NoteDraft Note=>request.Notes[0];internal long UiEpoch=>request.Epoch;
  internal long PreviewEpoch{get;private set;}internal BackupMergeWindow? Window{get;set;}internal BranchResolutionViewAuthority? View{get;private set;}internal BranchResolutionPreviewToken? Preview{get;set;}
  internal BranchResolutionOperation(MainWindow owner,BatchRequest request)
  {
   this.owner=owner;this.request=request;workspace=Session.Workspace;notes=workspace.Notes.ToArray();versions=notes.Select(n=>n.EditVersion).ToArray();PreviewEpoch=Session.AttachmentPreviewEpoch;
   cancellation=CancellationTokenSource.CreateLinkedTokenSource(owner.fileOperations.Token);token=cancellation.Token;
   try{owner.NotesList.SelectionChanged+=SelectionChanged;foreach(var note in notes)note.PropertyChanged+=SourceChanged;workspace.AttachmentReadInvalidating+=Invalidating;Session.Changed+=Changed;Session.Conceal+=Dispose;owner.Closed+=Closed;}
   catch{Dispose();throw;}
  }
  private bool EpochCurrent()=>Session.AttachmentPreviewEpoch==PreviewEpoch||Session.AttachmentPreviewEpoch==PreviewEpoch+1&&(checkpoint&&checkpointExpected&&!checkpointObserved&&!workspace.IsMergePublicationActive||View?.IsCheckpointActive==true&&!checkpointObserved||applying&&!applyObserved&&workspace.IsBranchResolutionPublicationActive);
  private bool OwnPublicationClear()=>applying&&applyObserved&&workspace.IsBranchResolutionPublicationActive;
  internal bool Current()=>!revoked&&!token.IsCancellationRequested&&!owner.windowClosed&&!owner.concealing&&owner.CurrentBatch(request)&&EpochCurrent()&&(!settled||applying||!Session.IsDirty)&&workspace.Notes.Count==notes.Length&&!notes.Where((n,i)=>n.IsClosed||n.EditVersion!=versions[i]||!workspace.Notes.Contains(n)).Any()&&owner.NotesList.SelectedItems.Count==1&&ReferenceEquals(owner.NotesList.SelectedItem,Note)&&(Window is null||!Window.IsRevoked&&(!presented||Window.IsVisible||OwnPublicationClear()));
  internal void MarkPresented()=>presented=true;
  internal void BeginCheckpoint(){checkpoint=true;checkpointExpected=Session.IsDirty;checkpointObserved=false;}internal void EndCheckpoint(){checkpoint=false;}internal void Settle()=>settled=true;
  internal void RegisterClosed(){closed=(_,_)=>Dispose();Window!.Closed+=closed;}
  internal void InvalidateSelection(){selectionGeneration++;selectedTip=Guid.Empty;Preview=null;var old=View;View=null;try{old?.Dispose();}catch{Dispose();}}
  internal BranchResolutionViewAuthority BeginSelection(Guid tip)
  {
   if(!Current()||tip==Guid.Empty)throw new InvalidOperationException("Branch selection ended");selectedTip=tip;long selectedGeneration=selectionGeneration;
   var view=Session.CreateBranchResolutionView(()=>SelectedPure(selectedGeneration,tip),owner.Dispatcher.CheckAccess);View=view;return view;
  }
  private bool SelectedPure(long generation,Guid tip)=>Current()&&generation==selectionGeneration&&tip==selectedTip&&(OwnPublicationClear()||Window is{IsVisible:true}&&Window.BranchesList.SelectedItem is BackupMergeBranchComparison selected&&selected.NoteId==Note.Id&&selected.Incoming.RevisionId==tip);
  internal bool SelectedCurrent(BranchResolutionViewAuthority view)=>ReferenceEquals(View,view)&&!view.Closed&&SelectedPure(selectionGeneration,selectedTip);
  internal void RetireSelection(BranchResolutionViewAuthority view){if(ReferenceEquals(View,view))InvalidateSelection();else view.Dispose();}
  internal void BeginApply(){applying=true;applyObserved=false;}internal void EndApply()=>applying=false;
  private void Invalidating(NoteDraft? source)
  {
   if(!revoked&&source is null&&checkpoint&&checkpointExpected&&!checkpointObserved&&!workspace.IsMergePublicationActive&&Session.AttachmentPreviewEpoch==PreviewEpoch+1){checkpointObserved=true;PreviewEpoch++;if(!Current())Dispose();return;}
   if(!revoked&&source is null&&View?.IsCheckpointActive==true&&!checkpointObserved&&Session.AttachmentPreviewEpoch==PreviewEpoch+1){checkpointObserved=true;PreviewEpoch++;if(!Current())Dispose();return;}
   if(!revoked&&ReferenceEquals(source,Note)&&applying&&!applyObserved&&workspace.IsBranchResolutionPublicationActive&&Session.AttachmentPreviewEpoch==PreviewEpoch+1)
   {
    applyObserved=true;PreviewEpoch++;
    if(!Current()||Window?.ClearResolutionPublication()!=true||!Current())Dispose();return;
   }
   Dispose();
  }
  private void SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>Dispose();private void SourceChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(NoteDraft.EditVersion))Dispose();}private void Changed(){if(!Current())Dispose();}private void Closed(object? sender,EventArgs e)=>Dispose();
  public void Dispose()
  {
   if(revoked)return;revoked=true;owner.NotesList.SelectionChanged-=SelectionChanged;foreach(var note in notes)note.PropertyChanged-=SourceChanged;workspace.AttachmentReadInvalidating-=Invalidating;Session.Changed-=Changed;Session.Conceal-=Dispose;owner.Closed-=Closed;
   if(Window is{ } viewer&&closed is{ } handler)viewer.Closed-=handler;
   try{cancellation.Cancel();}catch{}finally{cancellation.Dispose();}
   var view=View;View=null;Preview=null;try{view?.Dispose();}finally{if(Window is{ } attached){owner.branchResolutionOperations.Remove(attached);owner.backupMergeWindows.Remove(attached);attached.Dispose();}}
  }
 }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Transfer;
using MemoApp.Core.Editing;
using Microsoft.Win32;
namespace MemoApp.Windows;
public sealed partial class AttachmentPanel
{
 private InstalledOcrBackend ocrBackend=new();private string? ocrModels;
 private readonly Button modelInstall=new(){Content="OCR 모델 설치",Padding=new(6,3,6,3)};
 private readonly Button modelChoose=new(){Content="설치 모델 선택",Padding=new(6,3,6,3),Margin=new(6,0,0,0)};
 public Button OcrButton{get;}=new(){Content="선택 PNG 글자 추출",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
 public Button OcrCancelButton{get;}=new(){Content="OCR 취소·결과 지우기",Padding=new(6,3,6,3),Margin=new(6,0,0,0)};
 public Button OcrApplyButton{get;}=new(){Content="OCR 결과를 새 메모로",Padding=new(6,3,6,3),IsEnabled=false};
 public Button OcrLinkRecognizeButton{get;}=new(){Content="첨부 검색용 글자 추출",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
 public Button OcrLinkApplyButton{get;}=new(){Content="이 첨부의 검색에 저장",Padding=new(6,3,6,3),Margin=new(6,0,0,0),IsEnabled=false};
 public TextBox OcrResult{get;}=new(){IsReadOnly=true,IsUndoEnabled=false,TextWrapping=TextWrapping.Wrap,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};
 private string? ocrText;private Func<bool>? ocrAllowed;
 private SaveCoordinator? ocrObservationSession;
 private LinkedOcrView? linkedOcr,applyingLinkedOcr;
 private Task linkedOcrCleanup=Task.CompletedTask;
 internal Task WhenLinkedOcrCleanupIdle=>linkedOcrCleanup;
 private sealed class LinkedOcrView(SaveCoordinator owner,NoteDraft note,Guid id,long version,long epoch,AttachmentOcrReadGrant grant,Func<bool> allowed)
 {
  internal readonly SaveCoordinator Owner=owner;internal readonly NoteDraft Note=note;internal readonly Guid Id=id;internal readonly long Version=version,Epoch=epoch;
  internal readonly AttachmentOcrReadGrant Grant=grant;internal readonly Func<bool> Allowed=allowed;
  internal LinkedOcrStarter? Starter;internal OwnedOcrDerivation? Result;internal bool Retired;
  internal void Retire()
  {
   if(Retired)return;Retired=true;
   try{Owner.RetireAttachmentOcrGrant(Grant);}finally{try{Result?.Dispose();}finally{Starter?.Dispose();}}
  }
 }
 private void AddOcrControls(StackPanel content)
 {
  var buttons=new WrapPanel();foreach(var button in new[]{modelInstall,modelChoose,OcrButton,OcrLinkRecognizeButton,OcrCancelButton,OcrApplyButton,OcrLinkApplyButton})buttons.Children.Add(button);content.Children.Add(buttons);
  content.Children.Add(new TextBlock{Text="로컬 OCR: 모델 설치/선택 후 이 창에서만 사용합니다. 모델 5.53 MiB · Apache 2.0. PNG만 지원하며 인식 오류가 있을 수 있습니다. 결과를 확인한 뒤 새 암호 메모로 적용하세요. 자동 다운로드·외부 전송 없음.",TextWrapping=TextWrapping.Wrap});content.Children.Add(OcrResult);
  content.Children.Add(new TextBlock{Text="검색용 글자 추출은 결과 확인 후 이 메모의 선택 첨부에만 검색 정보를 저장합니다. 원래 본문·이미지는 유지하며 같은 이미지가 있는 다른 메모에는 적용하지 않습니다. 인식 오류가 있을 수 있습니다. 이미 저장한 검색 결과는 자동으로 교체하지 않습니다.",TextWrapping=TextWrapping.Wrap});
  modelInstall.Click+=InstallModelsClicked;modelChoose.Click+=ChooseModelsClicked;OcrButton.Click+=OcrClicked;OcrCancelButton.Click+=OcrCancelClicked;OcrApplyButton.Click+=OcrApplyClicked;OcrLinkRecognizeButton.Click+=OcrLinkRecognizeClicked;OcrLinkApplyButton.Click+=OcrLinkApplyClicked;
  ocrObservationSession=session;ocrObservationSession!.Workspace.AttachmentReadInvalidating+=OcrSourceInvalidating;
 }
 private void RefreshOcr()
 {
  foreach(var button in new[]{modelInstall,modelChoose})try{button.IsEnabled=Current()&&!busy&&ocrBackend.Available;}catch{}
  try{OcrButton.IsEnabled=Current()&&!busy&&ocrBackend.Available&&ocrModels is not null&&FilesList.SelectedItem is Entry;}catch{}
  try{OcrLinkRecognizeButton.IsEnabled=Current()&&!busy&&ocrBackend.Available&&ocrModels is not null&&FilesList.SelectedItem is Entry{Mime:"image/png"};}catch{}
  try{OcrApplyButton.IsEnabled=Current()&&!busy&&ocrText is not null&&ocrAllowed?.Invoke()==true;}catch{}
  try{OcrLinkApplyButton.IsEnabled=Current()&&!busy&&linkedOcr is{Retired:false,Result:not null} view&&view.Allowed()&&!view.Note.Metadata.AttachmentOcrResults.Any(r=>r.ObjectId==view.Id);}catch{}
  if(!Current()||linkedOcr is{ } stale&&!stale.Allowed())ClearOcr();
 }
 private void OcrReport(string message){try{Report(message);}catch{}}
 private void ClearOcr()
 {
  var old=linkedOcr;linkedOcr=null;try{old?.Retire();}catch{}try{applyingLinkedOcr?.Retire();}catch{}ClearOcrNative();
 }
 private bool ClearOcrNative()
 {
  ocrAllowed=null;ocrText=null;bool cleared=true;
  foreach(Action clear in new Action[]{()=>OcrApplyButton.IsEnabled=false,()=>OcrLinkApplyButton.IsEnabled=false,()=>OcrResult.Visibility=Visibility.Collapsed,()=>OcrResult.Text="",()=>OcrResult.IsUndoEnabled=false})try{clear();}catch{cleared=false;}
  return cleared;
 }
 private void OcrSourceInvalidating(NoteDraft? source)
 {
  // Core's own apply already owns the candidate. Clear visible text without retiring that legitimate publication.
  // A real selection/cancel/host-close callback still calls ClearOcr and retires its exact applying permit.
  if(applyingLinkedOcr is{Retired:false} applying&&ReferenceEquals(source,applying.Note)&&applying.Owner.Workspace.IsOcrPublicationActive&&applying.Owner.AttachmentPreviewEpoch==applying.Epoch+1){if(!ClearOcrNative())try{applying.Retire();}catch{}return;}
  InvalidatePreview();
 }
 private string? ChooseOcrDirectory()
 {
  var owner=Window.GetWindow(this);if(owner is null)return null;var dialog=new OpenFolderDialog{Multiselect=false,Title="암호 저장 폴더 밖의 OCR 모델 폴더 선택"};return dialog.ShowDialog(owner)==true?dialog.FolderName:null;
 }
 private async void InstallModelsClicked(object sender,RoutedEventArgs e)=>await ConfigureOcrModelsAsync(true,()=>
 {
  var owner=Window.GetWindow(this);if(owner is null)return null;
  if(MessageBox.Show(owner,"공식 한글·영문 모델과 Apache 2.0 안내(약 5.53 MiB)를 선택 폴더의 새 하위 폴더에 설치합니다. 취소·실패 시 공개 모델의 일부 폴더가 남을 수 있습니다. 모델을 설치하고 이 창에서 사용할까요?","로컬 OCR 모델",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return null;
  return ChooseOcrDirectory();
 });
 private async void ChooseModelsClicked(object sender,RoutedEventArgs e)=>await ConfigureOcrModelsAsync(false,ChooseOcrDirectory);
 public async Task<bool> ConfigureOcrModelsAsync(bool install,Func<string?> choose)
 {
  Dispatcher.VerifyAccess();if(!Current()||busy||!ocrBackend.Available||FilesList.SelectedItem is not Entry selected)return false;
  var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch;var token=cancellation.Token;
  bool Allowed()=>Same(active,source,version)&&!token.IsCancellationRequested&&active.IsAttachmentPreviewCurrent(source,selected.Id,version,epoch)&&FilesList.SelectedItem is Entry entry&&entry.Id==selected.Id;
  busy=true;try
  {
   RefreshOcr();if(!Allowed())return false;string? parent=choose();if(parent is null||!Allowed())return false;
   string protectedRoot=active.AttachmentExportRoot(source,selected.Id,version,epoch);
   parent=Path.GetDirectoryName(AttachmentFileTransfer.ResolveDestination(Path.Combine(parent,"ocr-public-model-marker"),protectedRoot))!;
   if(!Allowed())return false;var backend=ocrBackend;string result=await Task.Run(()=>{if(install)return backend.Install(parent,token);backend.Check(parent,token);return parent;},token);
   if(!Allowed())return false;InvalidatePreview();ocrModels=result;OcrReport("고정 한글·영문 모델을 확인했습니다. 이 창의 선택 PNG에서 OCR을 실행할 수 있습니다.");return Current();
  }
  catch(OperationCanceledException){return false;}
  catch{OcrReport("OCR 모델 설치·선택 실패 — 암호 저장 폴더 밖의 일반 로컬 폴더와 모델 해시를 확인하세요. 공개 모델의 일부 폴더가 남을 수 있습니다.");return false;}
  finally{busy=false;if(!IsDisposed)RefreshOcr();}
 }
 private async void OcrClicked(object sender,RoutedEventArgs e)=>await RecognizeSelectedAsync();
 private void OcrCancelClicked(object sender,RoutedEventArgs e){InvalidatePreview();if(!IsDisposed)RefreshOcr();}
 public async Task<bool> RecognizeSelectedAsync()
 {
  Dispatcher.VerifyAccess();if(!Current()||busy||!ocrBackend.Available||ocrModels is null||FilesList.SelectedItem is not Entry selected)return false;
  if(!ImagePreviewAdmission.TryEnter(Dispatcher)){OcrReport("다른 이미지/OCR 처리 중입니다. 처리 후 다시 실행하세요.");return false;}
  AttachmentReadLease? lease=null;CancellationTokenSource? cancel=null;OcrOperation? operation=null;OwnedOcrText? result=null;
  try
  {
   ImagePreviewAdmission.ClearDisplayed();InvalidatePreview();var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,generation=previewGeneration;observedPreviewEpoch=epoch;
   cancel=new();previewCancellation=cancel;var token=cancel.Token;
   bool Allowed()=>Same(active,source,version)&&!token.IsCancellationRequested&&generation==previewGeneration&&FilesList.SelectedItem is Entry item&&item.Id==selected.Id&&active.IsAttachmentPreviewCurrent(source,selected.Id,version,epoch);
   if(!Allowed())return false;lease=active.CreateAttachmentReadLease(source,selected.Id,version);if(!Allowed())return false;
   operation=await StartOcrWorker(ocrBackend,lease,ocrModels,token);result=await operation.Completion;
   if(!Allowed())return false;string text=result.Text;if(!Allowed())return false;
   ImagePreviewAdmission.PublishHost(this);ocrText=text;ocrAllowed=Allowed;
   OcrResult.Text=text;if(!Allowed()){InvalidatePreview();return false;}OcrResult.Visibility=Visibility.Visible;
   if(!Allowed()){InvalidatePreview();return false;}OcrApplyButton.IsEnabled=true;if(!Allowed()){InvalidatePreview();return false;}
   return true;
  }
  catch(OperationCanceledException){return false;}
  catch{if(!IsDisposed){InvalidatePreview();OcrReport("OCR 실패 — 제한 PNG·모델·시간/출력 한도를 확인하세요. 원본은 보존했습니다.");}return false;}
  finally
  {
   result?.Dispose();lease?.Dispose();if(ocrText is null&&ReferenceEquals(previewCancellation,cancel))previewCancellation=null;
   // Successful publication must remain valid until source revocation, not merely request completion.
   if(ocrText is null)cancel?.Dispose();
   if(operation is null)ImagePreviewAdmission.Exit();else _=ReleaseOcrAdmission(operation.Settled);
  }
 }
 private static Task<OcrOperation> StartOcrWorker(InstalledOcrBackend backend,AttachmentReadLease lease,string models,CancellationToken token)=>Task.Run(()=>backend.Start(lease,models,token));
 private static async Task ReleaseOcrAdmission(Task settled)
 {try{await settled.ConfigureAwait(false);ImagePreviewAdmission.Exit();}catch{/* Failed cleanup remains fail-closed; no private result logging. */}}
 private async void OcrLinkRecognizeClicked(object sender,RoutedEventArgs e)=>await RecognizeLinkedSelectedAsync();
 public async Task<bool> RecognizeLinkedSelectedAsync()
 {
  Dispatcher.VerifyAccess();if(!Current()||busy||!ocrBackend.Available||ocrModels is null||FilesList.SelectedItem is not Entry{Mime:"image/png"} selected)return false;
  if(!ImagePreviewAdmission.TryEnter(Dispatcher)){OcrReport("다른 이미지/OCR 처리 중입니다. 처리 후 다시 실행하세요.");return false;}
  LinkedOcrView? view=null;AttachmentOcrReadGrant? issued=null;SaveCoordinator? issuedOwner=null;LinkedOcrOperation? operation=null;OwnedOcrDerivation? candidate=null;CancellationTokenSource? cancel=null;bool published=false;busy=true;
  try
  {
   ImagePreviewAdmission.ClearDisplayed();InvalidatePreview();var active=session!;var source=note!;long version=source.EditVersion,epoch=active.AttachmentPreviewEpoch,generation=previewGeneration;observedPreviewEpoch=epoch;
   cancel=new();previewCancellation=cancel;var token=cancel.Token;
   bool Allowed()=>Same(active,source,version)&&!token.IsCancellationRequested&&generation==previewGeneration&&FilesList.SelectedItem is Entry item&&item.Id==selected.Id&&active.IsAttachmentPreviewCurrent(source,selected.Id,version,epoch);
   if(!Allowed())return false;issuedOwner=active;issued=active.CreateAttachmentOcrReadGrant(source,selected.Id,version,epoch,Dispatcher.CheckAccess);var grant=issued;
   view=new(active,source,selected.Id,version,epoch,grant,Allowed);linkedOcr=view;issued=null;issuedOwner=null;
   RefreshOcr();if(!Allowed()||view.Retired)return false;
   // Source decode creates only detached input. Core binds the preallocated operation before native work is queued.
   view.Starter=ocrBackend.PrepareLinked(grant,ocrModels,token);if(!Allowed()||view.Retired)return false;
   operation=active.StartAttachmentOcrGrant(grant,view.Starter);
   candidate=await operation.Completion;if(!Allowed()||view.Retired)return false;
   await operation.Settled;if(!Allowed()||view.Retired)return false;
   active.SettleAttachmentOcrGrant(grant);if(!Allowed()||view.Retired)return false;
   string text=candidate.Text;if(!Allowed()||view.Retired)return false;
   view.Result=candidate;candidate=null;ocrText=text;ocrAllowed=Allowed;ImagePreviewAdmission.PublishHost(this);
   OcrResult.Text=text;if(!Allowed()||view.Retired){InvalidatePreview();return false;}
   OcrResult.Visibility=Visibility.Visible;if(!Allowed()||view.Retired){InvalidatePreview();return false;}
   OcrApplyButton.IsEnabled=true;if(!Allowed()||view.Retired){InvalidatePreview();return false;}
   OcrLinkApplyButton.IsEnabled=!source.Metadata.AttachmentOcrResults.Any(r=>r.ObjectId==selected.Id);if(!Allowed()||view.Retired){InvalidatePreview();return false;}
   published=true;return true;
  }
  catch(OperationCanceledException){return false;}
  catch{if(!IsDisposed){InvalidatePreview();OcrReport("검색용 OCR 실패 — 선택 PNG·모델·시간/출력 한도를 확인하세요. 원본 본문과 첨부는 보존했습니다.");}return false;}
  finally
  {
   if(issued is not null)try{issuedOwner!.RetireAttachmentOcrGrant(issued);}catch{}
   candidate?.Dispose();
   if(!published)
   {
    if(ReferenceEquals(linkedOcr,view))linkedOcr=null;try{view?.Retire();}catch{}
    if(ReferenceEquals(previewCancellation,cancel))previewCancellation=null;cancel?.Dispose();
   }
   // A failed start can still have a registered skeleton operation; drain that exact task pair too.
   operation??=view?.Starter?.Operation;
   if(operation is null)ImagePreviewAdmission.Exit();
   else if(published)ImagePreviewAdmission.Exit(); // Both real tasks completed successfully before publication.
   else
   {
    var drain=DrainLinkedResultAsync(operation);
    if(view is not null)linkedOcrCleanup=ObserveLinkedRetirementAsync(view.Owner,view.Grant,drain);
   }
   busy=false;if(!IsDisposed)RefreshOcr();
  }
 }
 // Detached cleanup survives a closed/shutting-down native host. It retains no note/session/authority callback.
 private static async Task DrainLinkedResultAsync(LinkedOcrOperation operation)
 {
  try{try{await operation.Completion.ConfigureAwait(false);}catch{}try{await operation.Settled.ConfigureAwait(false);}catch{}}
  finally
  {
   if(operation.Completion.IsCompletedSuccessfully)operation.Completion.Result.Dispose();
   if(operation.Completion.IsCompleted&&operation.Settled.IsCompletedSuccessfully)ImagePreviewAdmission.Exit();
  }
 }
 // Owner continuation only: workers never receive the coordinator or retirement callback.
 private static async Task ObserveLinkedRetirementAsync(SaveCoordinator active,AttachmentOcrReadGrant grant,Task drain)
 {try{await drain;active.SettleAttachmentOcrGrant(grant);}catch{/* Owner shutdown or failed cleanup remains revoked; never log private data. */}}
 private async void OcrLinkApplyClicked(object sender,RoutedEventArgs e)=>await ApplyLinkedOcrResultAsync();
 public async Task<bool> ApplyLinkedOcrResultAsync()
 {
  Dispatcher.VerifyAccess();if(!Current()||busy||linkedOcr is not{Retired:false,Result:{ } result} view||!view.Allowed())return false;
  if(view.Note.Metadata.AttachmentOcrResults.Any(r=>r.ObjectId==view.Id)){OcrReport("이 메모의 선택 첨부에는 검색 결과가 이미 저장되어 있습니다. 자동으로 교체하지 않습니다.");ClearOcr();return false;}
  bool applied=false;linkedOcr=null;view.Result=null;applyingLinkedOcr=view;busy=true;
  bool StableHost()=>Current()&&ReferenceEquals(session,view.Owner)&&ReferenceEquals(note,view.Note);
  try
  {
   if(!ClearOcrNative()||view.Retired||!view.Allowed())return false;
   var outcome=view.Owner.ApplyAttachmentOcrResult(view.Note,view.Id,view.Version,view.Epoch,result);
   applied=outcome==AttachmentOcrApplyOutcome.AppliedDirty;if(!applied){OcrReport("선택 첨부의 검색 정보를 적용하지 못했습니다. 원본 변경·보기·선택·전체 저장 한도를 확인하세요.");return false;}
   // The authoritative receipt survives our own metadata version/epoch invalidation; no old preview is refreshed.
   if(view.Owner.IsLocked)return false;bool saved=await view.Owner.SaveAsync();
   if(StableHost())OcrReport(saved&&!view.Owner.IsDirty?"이 메모의 선택 첨부에 OCR 검색 정보를 암호 저장했습니다. 본문·원본 이미지는 유지했습니다.":"첨부 검색 정보를 반영했지만 암호 저장을 완료하지 못했습니다. 변경을 유지합니다. 저장 상태를 확인하세요.");return saved&&!view.Owner.IsLocked&&!view.Owner.IsDirty&&StableHost();
  }
  catch{if(StableHost())OcrReport(applied?"첨부 검색 정보는 반영됐지만 저장 완료를 확인하지 못했습니다. 변경과 원본을 유지합니다.":"첨부 검색 정보를 적용하지 못했습니다. 원본과 현재 내용을 유지했습니다.");return false;}
  finally{result.Dispose();try{view.Retire();}catch{}if(ReferenceEquals(applyingLinkedOcr,view))applyingLinkedOcr=null;busy=false;if(!IsDisposed)RefreshOcr();}
 }
 private async void OcrApplyClicked(object sender,RoutedEventArgs e)=>await ApplyOcrResultAsync();
 public async Task<bool> ApplyOcrResultAsync()
 {
  Dispatcher.VerifyAccess();if(!Current()||busy||ocrText is not { } text||ocrAllowed is not { } allowed||!allowed())return false;
  var active=session!;var before=active.Workspace.Notes.Select(n=>n.Id).ToHashSet();ClearOcr();if(!allowed())return false;
  NoteDraft? added=null;try
  {
   added=active.Workspace.ImportText("OCR 결과",text);if(active.IsLocked||added.IsClosed)return false;
   bool saved=await active.SaveAsync();OcrReport(saved&&!active.IsDirty?"OCR 결과를 새 메모로 암호 저장했습니다. 원본 첨부는 보존했습니다.":"OCR 새 메모를 추가했지만 저장 완료를 확인하지 못했습니다. 저장 상태를 확인하세요.");return saved&&!active.IsDirty;
  }
  catch
  {
   if(!active.IsLocked&&added is null)added=active.Workspace.Notes.FirstOrDefault(n=>!before.Contains(n.Id)&&n.Title=="OCR 결과"&&n.Text==text);
   OcrReport(added is null?"OCR 결과를 적용하지 못했습니다. 메모·본문·저장/이력 한도를 확인하세요.":"OCR 새 메모는 추가됐지만 저장 완료를 확인하지 못했습니다. 기존 내용과 추가 메모를 보존했습니다.");return false;
  }
 }
 private void DisposeOcr()
 {
  ClearOcr();ocrModels=null;modelInstall.Click-=InstallModelsClicked;modelChoose.Click-=ChooseModelsClicked;OcrButton.Click-=OcrClicked;OcrCancelButton.Click-=OcrCancelClicked;OcrApplyButton.Click-=OcrApplyClicked;OcrLinkRecognizeButton.Click-=OcrLinkRecognizeClicked;OcrLinkApplyButton.Click-=OcrLinkApplyClicked;
  if(ocrObservationSession is{ } observed){observed.Workspace.AttachmentReadInvalidating-=OcrSourceInvalidating;ocrObservationSession=null;}
  foreach(var button in new[]{modelInstall,modelChoose,OcrButton,OcrLinkRecognizeButton,OcrLinkApplyButton})try{button.IsEnabled=false;}catch{}
 }
}

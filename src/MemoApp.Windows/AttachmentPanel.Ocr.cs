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
 public TextBox OcrResult{get;}=new(){IsReadOnly=true,IsUndoEnabled=false,TextWrapping=TextWrapping.Wrap,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};
 private string? ocrText;private Func<bool>? ocrAllowed;
 private void AddOcrControls(StackPanel content)
 {
  var buttons=new WrapPanel();foreach(var button in new[]{modelInstall,modelChoose,OcrButton,OcrCancelButton,OcrApplyButton})buttons.Children.Add(button);content.Children.Add(buttons);
  content.Children.Add(new TextBlock{Text="로컬 OCR: 모델 설치/선택 후 이 창에서만 사용합니다. 모델 5.53 MiB · Apache 2.0. PNG만 지원하며 인식 오류가 있을 수 있습니다. 결과를 확인한 뒤 새 암호 메모로 적용하세요. 자동 다운로드·외부 전송 없음.",TextWrapping=TextWrapping.Wrap});content.Children.Add(OcrResult);
  modelInstall.Click+=InstallModelsClicked;modelChoose.Click+=ChooseModelsClicked;OcrButton.Click+=OcrClicked;OcrCancelButton.Click+=OcrCancelClicked;OcrApplyButton.Click+=OcrApplyClicked;
 }
 private void RefreshOcr()
 {
  foreach(var button in new[]{modelInstall,modelChoose})try{button.IsEnabled=Current()&&!busy&&ocrBackend.Available;}catch{}
  try{OcrButton.IsEnabled=Current()&&!busy&&ocrBackend.Available&&ocrModels is not null&&FilesList.SelectedItem is Entry;}catch{}
  try{OcrApplyButton.IsEnabled=Current()&&!busy&&ocrText is not null&&ocrAllowed?.Invoke()==true;}catch{}
 }
 private void OcrReport(string message){try{Report(message);}catch{}}
 private void ClearOcr()
 {
  ocrAllowed=null;ocrText=null;try{OcrApplyButton.IsEnabled=false;}catch{}try{OcrResult.Visibility=Visibility.Collapsed;}catch{}try{OcrResult.Text="";OcrResult.IsUndoEnabled=false;}catch{}
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
  ClearOcr();ocrModels=null;modelInstall.Click-=InstallModelsClicked;modelChoose.Click-=ChooseModelsClicked;OcrButton.Click-=OcrClicked;OcrCancelButton.Click-=OcrCancelClicked;OcrApplyButton.Click-=OcrApplyClicked;
  foreach(var button in new[]{modelInstall,modelChoose,OcrButton})try{button.IsEnabled=false;}catch{}
 }
}

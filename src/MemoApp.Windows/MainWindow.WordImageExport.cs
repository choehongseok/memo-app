using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;

namespace MemoApp.Windows;

public partial class MainWindow
{
 internal async Task<bool> ExportSelectedWordImagesAsync(Func<bool> confirm,Func<string?> choose,IAtomicVaultFiles? files=null)
 {
  Dispatcher.VerifyAccess();if(windowClosed||concealing||officeExportBusy||CaptureBatch(false) is not{ } request)return false;
  using var operation=new WordExportOperation(this,request);OwnedWordImageContext? context=null;WordImageBuildOperation? build=null;PreparedTextExport? prepared=null;officeExportBusy=true;
  try
  {
   UpdateSelectedActions();if(!operation.Current()||!confirm()||!operation.Current())return false;
   string? destination=choose();if(destination is null||!operation.Current())return false;
   if(!Path.GetExtension(destination).Equals(".docx",StringComparison.OrdinalIgnoreCase))throw new IOException("Word extension mismatch");
   string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(destination));if(relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative))throw new IOException("Plaintext Word inside active vault refused");
   // The owner captures/authenticates once. No note, vault, dispatcher or source callback enters the fixed builder.
   context=request.Session.CaptureWordImageContext(request.Notes,operation.Versions,operation.PreviewEpoch,operation.Token);operation.Context=context;
   if(!operation.Current())return false;
   build=StructuredWordExport.StartOwned(context,operation.Token);
   prepared=await build.Completion;await build.Settled;
   if(!operation.Current())return false;
   // Keep the package alive through the actual file worker, including blocked CreateNew/flush/cancellation.
   await WriteWordImageExportAsync(prepared,destination,operation.Token,files);
   if(!operation.Current())return false;
   Notice.Text=$"{request.Notes.Length}개 메모의 지원 글자·목록·표와 인라인 PNG를 평문 Word로 복사했습니다. 독립 첨부·OCR 검색 정보·원본 이력은 포함하지 않습니다. 평문 파일과 뷰어 캐시는 앱 잠금 뒤에도 남을 수 있습니다.";
   return operation.Current();
  }
  catch(OperationCanceledException){return false;}
  catch{if(operation.Current())try{Notice.Text="Word 내보내기 실패 — 원본과 기존 대상은 보존했습니다. 지원 문서·인라인 PNG·전체 한도·새 파일 이름·외부 로컬 폴더를 확인하세요. 실패한 부분 평문 파일은 남을 수 있습니다.";}catch{}return false;}
  finally
  {
   operation.Revoke();
   if(build is not null)
   {
    // Cancellation cannot substitute for actual detached builder completion and teardown.
    try{var late=await build.Completion;if(!ReferenceEquals(late,prepared))late.Dispose();}catch{}
    try{await build.Settled;}catch{}
   }
   prepared?.Dispose();
   if(context is not null)
   {
    context.Dispose();try{request.Session.SettleWordImageContext(context);}catch{/* Failed teardown keeps the owner's slot fail-closed. */}
   }
   officeExportBusy=false;if(!windowClosed&&!concealing)try{UpdateSelectedActions();}catch{}
  }
 }
 private static Task WriteWordImageExportAsync(PreparedTextExport prepared,string destination,CancellationToken token,IAtomicVaultFiles? files)=>Task.Run(()=>TextTransfer.WritePrepared(prepared,destination,token,files),token);
 // Owner-side monotonic lifetime only. This object is never passed to the detached worker.
 private sealed class WordExportOperation:IDisposable
 {
  private readonly MainWindow owner;private readonly BatchRequest request;private readonly EditingWorkspace workspace;private readonly CancellationTokenSource cancellation;
  private bool revoked,disposed;
  internal long[] Versions{get;}internal long PreviewEpoch{get;}internal CancellationToken Token{get;}internal OwnedWordImageContext? Context{get;set;}
  internal WordExportOperation(MainWindow owner,BatchRequest request)
  {
   this.owner=owner;this.request=request;workspace=request.Session.Workspace;Versions=request.Notes.Select(n=>n.EditVersion).ToArray();PreviewEpoch=request.Session.AttachmentPreviewEpoch;
   cancellation=CancellationTokenSource.CreateLinkedTokenSource(owner.fileOperations.Token);Token=cancellation.Token;
   try{request.Session.RegisterWordImageExportOwner(owner.Dispatcher.CheckAccess);owner.NotesList.SelectionChanged+=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged+=SourceChanged;workspace.AttachmentReadInvalidating+=Invalidating;request.Session.Changed+=Changed;request.Session.Conceal+=Revoke;owner.Closed+=Closed;}
   catch{Dispose();throw;}
  }
  internal bool Current()=>!revoked&&!disposed&&!Token.IsCancellationRequested&&!owner.windowClosed&&!owner.concealing&&owner.CurrentBatch(request)&&ReferenceEquals(request.Session.Workspace,workspace)&&PreviewEpoch==request.Session.AttachmentPreviewEpoch&&!request.Notes.Where((n,i)=>n.EditVersion!=Versions[i]).Any()&&owner.NotesList.SelectedItems.Cast<NoteDraft>().SequenceEqual(request.Notes)&&(Context is null||request.Session.IsWordImageContextCurrent(Context));
  internal void Revoke()
  {
   if(revoked)return;revoked=true;try{cancellation.Cancel();}catch{}
   if(Context is{ } context){try{request.Session.RetireWordImageContext(context);}catch{context.Dispose();}}
  }
  private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>Revoke();private void SourceChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(NoteDraft.EditVersion))Revoke();}private void Invalidating(NoteDraft? note)=>Revoke();private void Changed(){if(!Current())Revoke();}private void Closed(object? sender,EventArgs e)=>Revoke();
  public void Dispose()
  {
   if(disposed)return;Revoke();disposed=true;owner.NotesList.SelectionChanged-=SelectionChanged;foreach(var note in request.Notes)note.PropertyChanged-=SourceChanged;workspace.AttachmentReadInvalidating-=Invalidating;request.Session.Changed-=Changed;request.Session.Conceal-=Revoke;owner.Closed-=Closed;cancellation.Dispose();Context=null;
  }
 }
}

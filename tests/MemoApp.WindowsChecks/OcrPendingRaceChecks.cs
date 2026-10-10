using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;

internal static partial class Program
{
 // Synthetic generic-backend lifecycle evidence only; no engine/provenance or accuracy claim.
 private static async Task OcrPendingRaceRun()
 {
  foreach(string scenario in new[]{"selection","edit","epoch","cancel","lock-fault","dispose","settled-first"})
   await OcrPendingRaceCase(scenario);
 }

 private sealed class PendingRaceBackend:InstalledOcrBackend
 {
  internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal readonly TaskCompletionSource<OwnedOcrText> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal readonly TaskCompletionSource Settled=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal readonly byte[] Utf8=Encoding.UTF8.GetBytes("Synthetic late OCR text 한글 ABC 123");
  private readonly OcrOperation operation;
  private readonly OwnedOcrText candidate;
  private int started;
  internal PendingRaceBackend(){candidate=new(Utf8);operation=new(Completion.Task,Settled.Task);}
  internal override OcrOperation Start(AttachmentReadLease lease,string models,CancellationToken token)
  {
   try
   {
    if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("Synthetic operation already started");
    Require(lease.Consume(bytes=>Require(bytes.SequenceEqual(PreviewPng),"Pending backend reads the genuine source lease")),"Pending source lease consumed before revocation");
    Started.TrySetResult();return operation;
   }
   catch(Exception error){Started.TrySetException(error);throw;}
  }
  internal void Deliver(){if(!Completion.TrySetResult(candidate))candidate.Dispose();}
  internal void DisposeCandidate()=>candidate.Dispose();
 }

 private static async Task OcrPendingRaceCase(string scenario)
 {
  string root=Path.Combine(Path.GetTempPath(),"memo-wpf-ocr-pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  byte[] secret=EncryptedVault.GenerateRecoverySecret();var backend=new PendingRaceBackend();
  SaveCoordinator? session=null,probeSession=null;AttachmentPanel? panel=null,probe=null;Window? window=null;
  Task<bool>? recognizing=null;bool ownersDisposed=false;byte[]? cipher=null;
  DependencyPropertyDescriptor? descriptor=null;EventHandler? nativeFault=null;Button? modelChoose=null;
  try
  {
   session=new(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);
   var note=session.Workspace.CreateNote();note.Text="Original public synthetic body";
   Require(await session.PrepareAttachmentsAsync(),"Pending fixture root");session.AttachBytes(note,PreviewPng,"synthetic.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"Pending fixture saved source");
   probeSession=new(EncryptedVault.Create(Path.Combine(root,"probe"),secret,secret),TimeProvider.System);
   var probeNote=probeSession.Workspace.CreateNote();Require(await probeSession.PrepareAttachmentsAsync(),"Independent admission probe root");probeSession.AttachBytes(probeNote,PreviewPng,"probe.png","image/png",probeNote.EditVersion);Require(await probeSession.SaveAsync(),"Independent probe source saved");
   panel=new(session,note,()=>true,_=>{});probe=new(probeSession,probeNote,()=>true,_=>{});
   window=new(){Content=panel,Width=800,Height=650};window.Show();await Idle();panel.FilesList.SelectedIndex=probe.FilesList.SelectedIndex=0;
   Require(await panel.ConfigureOcrModelsAsync(true,()=>root),"Existing embedded public models explicitly installed; no download");SetField(panel,"ocrBackend",backend);
   cipher=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));
   recognizing=panel.RecognizeSelectedAsync();await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
   Require(!recognizing.IsCompleted&&!backend.Completion.Task.IsCompleted&&!backend.Settled.Task.IsCompleted,"Actual UI request awaits independently pending Completion and Settled");
   Require(!await probe.PreviewSelectedAsync(),"Another session cannot reuse global slot while Completion is pending");
   if(scenario=="settled-first")
   {
    backend.Settled.TrySetResult();await backend.Settled.Task;await Idle();
    Require(!recognizing.IsCompleted&&!await probe.PreviewSelectedAsync(),"Successful Settled alone cannot release a still-pending UI Completion");
   }
   switch(scenario)
   {
    case "selection":panel.FilesList.SelectedIndex=-1;panel.FilesList.SelectedIndex=0;break;
    case "edit":note.Text="Authorized public synthetic edit";break;
    case "epoch":long version=note.EditVersion;long epoch=session.AttachmentPreviewEpoch;session.Workspace.AcceptPrepared(session.Workspace.Capture());Require(note.EditVersion==version&&session.AttachmentPreviewEpoch>epoch,"Same-version source epoch really changes");break;
    case "cancel":case "settled-first":panel.OcrCancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;
    case "lock-fault":
     modelChoose=Field<Button>(panel,"modelChoose");descriptor=DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool faultObserved=false;
     nativeFault=(_,_)=>{if(!modelChoose.IsEnabled){faultObserved=true;throw new IOException("Synthetic pending native cleanup refusal");}};descriptor.AddValueChanged(modelChoose,nativeFault);
     await session.LockAsync();Require(faultObserved&&session.KeysReleased&&!session.IsBusy&&panel.IsDisposed,"Native cleanup callback failure still releases keys before pending Completion");break;
    case "dispose":panel.Dispose();window.Close();window=null;Require(panel.IsDisposed,"Pending host really disposed");break;
   }
   var expected=scenario=="lock-fault"?null:session.Workspace.Capture();
   long expectedVersion=note.EditVersion;var expectedModified=note.ModifiedAt;
   if(scenario=="edit")Require(session.IsDirty,"Authorized source edit remains dirty before late completion");
   Require(!recognizing.IsCompleted&&!await probe.PreviewSelectedAsync(),"Revocation cannot fake pending operation settlement");
   backend.Deliver();Require(!await recognizing.WaitAsync(TimeSpan.FromSeconds(10)),"Late synthetic Completion refuses stale UI publication: "+scenario);
   Require(backend.Utf8.All(b=>b==0)&&panel.OcrResult.Text.Length==0&&!panel.OcrApplyButton.IsEnabled&&!await panel.ApplyOcrResultAsync(),"Late actual owned UTF8 is zeroed and cannot create an OCR note: "+scenario);
   if(expected is not null)
   {
    var actual=session.Workspace.Capture();
    if(scenario=="edit")
    {
     Require(session.IsDirty&&note.EditVersion==expectedVersion&&note.ModifiedAt==expectedModified,"Late completion preserves exact live dirty edit version/time");
     // Capture assigns a new provisional revision ID on every dirty capture, without installing it.
     // Normalize only that target ID; all source content, parents, history and objects stay exact.
     Guid provisional=expected.Notes.Single(n=>n.NoteId==note.Id).RevisionId;
     actual=actual with{Notes=actual.Notes.Select(n=>n.NoteId==note.Id?n with{RevisionId=provisional}:n).ToArray()};
    }
    Require(JsonSerializer.Serialize(actual)==JsonSerializer.Serialize(expected),"Complete source preserved after the authorized race action: "+scenario);
   }
   Require(cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"vault","current.vault"))),"Late result never writes source ciphertext: "+scenario);
   if(scenario!="settled-first")Require(!await probe.PreviewSelectedAsync(),"Completed UI result still cannot release unsuccessfully unsettled cleanup");
   backend.Settled.TrySetResult();await backend.Settled.Task;
   bool restored=false;for(int i=0;i<100&&!restored;i++){restored=await probe.PreviewSelectedAsync();if(!restored)await Task.Delay(10);}
   Require(restored,"Other session gets the real slot only after both actual tasks finish: "+scenario);
  }
  finally
  {
   // Always complete this fixture's real handles, then drain actual UI work before disposing owners.
   // A timeout above is a test failure, never cleanup evidence or permission to abandon the request.
   var failures=new List<Exception>();
   void Attempt(Action action){try{action();}catch(Exception error){failures.Add(error);}}
   backend.Deliver();backend.Settled.TrySetResult();
   if(recognizing is not null)try{await recognizing;}catch(Exception error){failures.Add(error);}
   try{await backend.Completion.Task;}catch(Exception error){failures.Add(error);}
   try{await backend.Settled.Task;}catch(Exception error){failures.Add(error);}
   Attempt(backend.DisposeCandidate);
   if(descriptor is not null&&nativeFault is not null&&modelChoose is not null)Attempt(()=>descriptor.RemoveValueChanged(modelChoose,nativeFault));
   Attempt(()=>panel?.Dispose());Attempt(()=>probe?.Dispose());Attempt(()=>window?.Close());
   foreach(var owner in new[]{session,probeSession})if(owner is not null)
   {
    try{await owner.LockAsync();Require(owner.KeysReleased&&!owner.IsBusy,"Pending fixture actual owner lock settles");}catch(Exception error){failures.Add(error);}
    Attempt(owner.Dispose);
   }
   ownersDisposed=failures.Count==0;
   CryptographicOperations.ZeroMemory(secret);if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);
   Require(backend.Utf8.All(b=>b==0),"Pending fixture always clears its actual owned result buffer");
   if(ownersDisposed)Attempt(()=>Directory.Delete(root,true));
   if(failures.Count!=0)throw new AggregateException("Synthetic pending OCR fixture cleanup failed",failures);
  }
 }
}

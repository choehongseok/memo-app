using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Core.Search;
using MemoApp.Windows;
internal static partial class Program
{
 private static string OcrComparable(string value)=>string.Concat(value.Where(c=>!char.IsWhiteSpace(c)));
 private const string OcrExpected="한글 메모 시험\n합성 자료만 사용합니다\n저장 암호 잠금 복구\nABC 123\n";
 private static async Task OcrProductRun()
 {
  var root=Path.Combine(Path.GetTempPath(),"memo-wpf-ocr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
  byte[] png=Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64"));Window? window=null;
  try
  {
   using(var session=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System))
   {
    var note=session.Workspace.CreateNote();note.Text="원본 한글 본문";session.Workspace.ConvertMode(note,"markdown");Require(await session.PrepareAttachmentsAsync(),"OCR fixture encrypted root");session.AttachBytes(note,png,"합성.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"OCR source save");
    using var panel=new AttachmentPanel(session,note,()=>true,_=>{},Guid.NewGuid());window=new Window{Content=panel,Width=800,Height=800};window.Show();await Idle();panel.FilesList.SelectedIndex=0;
    string baseline=JsonSerializer.Serialize(session.Workspace.Capture());
    Require(!await panel.RecognizeSelectedAsync()&&panel.OcrResult.Text=="","OCR stays off until explicit model installation/selection");
    Require(!await panel.ConfigureOcrModelsAsync(true,()=>null)&&!Directory.EnumerateDirectories(root,"memo-ocr-models-*").Any(),"Model picker cancel creates no public model files");
    Require(!await panel.ConfigureOcrModelsAsync(true,()=>Path.Combine(root,"vault")),"Model installation refuses encrypted data root");
    Require(await panel.ConfigureOcrModelsAsync(true,()=>root),"Actual public model ZIP installed by explicit UI action");
    string models=Directory.EnumerateDirectories(root,"memo-ocr-models-*").Single();Require(Directory.GetFiles(models).Length==3,"Only exact kor/eng/Apache2 public files installed");
    Require(JsonSerializer.Serialize(session.Workspace.Capture())==baseline,"Model installation does not modify encrypted note/history/source");
    Require(await panel.RecognizeSelectedAsync()&&OcrComparable(panel.OcrResult.Text)==OcrComparable(OcrExpected)&&!panel.OcrResult.CanUndo,"Actual product native OCR publishes Korean/English read-only bounded preview without undo");
    Require(JsonSerializer.Serialize(session.Workspace.Capture())==baseline,"Actual OCR preview preserves complete source snapshot");
    Require(await panel.ApplyOcrResultAsync(),"Explicit OCR new plain note encrypted save");var created=session.Workspace.Notes.Single(n=>n.Id!=note.Id);
    Require(created.Mode=="plain"&&OcrComparable(created.Text)==OcrComparable(OcrExpected)&&note.Text=="원본 한글 본문"&&note.Mode=="markdown"&&note.AttachmentIds.Length==1,"OCR applies separate plain note and preserves original");
    Require(NoteSearch.Find(session.Workspace,new(){Query="ABC 123",Field=SearchField.Body}).SequenceEqual([created]),"Accepted OCR body participates in ordinary note search");
    Require(panel.OcrResult.Text==""&&!panel.OcrApplyButton.IsEnabled&&!await panel.ApplyOcrResultAsync(),"Consumed OCR result cannot apply twice");
    panel.FilesList.SelectedIndex=0;Require(await panel.RecognizeSelectedAsync(),"OCR can run again with explicitly selected session models");panel.OcrCancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Require(panel.OcrResult.Text==""&&!panel.OcrApplyButton.IsEnabled,"Cancel command clears owned/native preview");
    string linked=Path.Combine(root,"synthetic-public-path.txt");File.WriteAllText(linked,"public fixture");Require(await panel.ConnectFilePathAsync(()=>linked,_=>true),"Cleanup fixture retains a native path DTO");object row=panel.PathLinksList.Items[0];panel.FilesList.SelectedIndex=0;
    Require(await panel.RecognizeSelectedAsync(),"Fresh OCR after cancellation");
    var chooseButton=Field<System.Windows.Controls.Button>(panel,"modelChoose");var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(System.Windows.Controls.Button));bool disableFault=false;EventHandler fault=(_,_)=>{if(!chooseButton.IsEnabled){disableFault=true;throw new IOException("Synthetic OCR native disable callback");}};descriptor.AddValueChanged(chooseButton,fault);
    try{await session.LockAsync();Require(disableFault&&session.KeysReleased&&panel.IsDisposed&&panel.OcrResult.Text==""&&panel.OcrResult.Visibility==Visibility.Collapsed&&(string)row.GetType().GetProperty("Label")!.GetValue(row)! ==""&&panel.PathLinksList.Items.Count==0,"OCR disable exception cannot interrupt lock/path DTO/private label cleanup");}finally{descriptor.RemoveValueChanged(chooseButton,fault);}
    window.Close();window=null;
   }
   using(var reopened=new SaveCoordinator(EncryptedVault.Open(Path.Combine(root,"vault"),secret),TimeProvider.System))
   {
    var result=reopened.Workspace.Notes.Single(n=>n.Title=="OCR 결과");Require(OcrComparable(result.Text)==OcrComparable(OcrExpected)&&NoteSearch.Find(reopened.Workspace,new(){Query="ABC 123",Field=SearchField.Body}).SequenceEqual([result]),"Actual encrypted OCR result and body search survive restart");
    var original=reopened.Workspace.Notes.Single(n=>n.Id!=result.Id);using var again=new AttachmentPanel(reopened,original,()=>true,_=>{});again.FilesList.SelectedIndex=0;
    Require(!await again.RecognizeSelectedAsync(),"Restart does not silently enable model use");string models=Directory.EnumerateDirectories(root,"memo-ocr-models-*").Single();
    Require(await again.ConfigureOcrModelsAsync(false,()=>models)&&await again.RecognizeSelectedAsync(),"Existing fixed model files selected after restart and actual native OCR works");
    await reopened.LockAsync();Require(again.OcrResult.Text=="","Reopened OCR preview cleared on lock");
   }
  }
  finally{window?.Close();CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(png);Directory.Delete(root,true);}
 }
 private sealed class DeferredOcrBackend:InstalledOcrBackend
 {
  internal readonly TaskCompletionSource Settled=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal override OcrOperation Start(AttachmentReadLease lease,string models,CancellationToken token)
  {Started.TrySetResult();return new(Task.FromException<OwnedOcrText>(new TimeoutException("Synthetic boundary only")),Settled.Task);}
 }
 private static async Task OcrSettlementUiRun()
 {
  string root=Path.Combine(Path.GetTempPath(),"memo-wpf-ocr-settlement-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();
  var backend=new DeferredOcrBackend();
  try
  {
   using var session=new SaveCoordinator(EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret),TimeProvider.System);var note=session.Workspace.CreateNote();Require(await session.PrepareAttachmentsAsync(),"Settlement source root");session.AttachBytes(note,PreviewPng,"fixture.png","image/png",note.EditVersion);Require(await session.SaveAsync(),"Settlement baseline save");
   using var first=new AttachmentPanel(session,note,()=>true,_=>{});using var second=new AttachmentPanel(session,note,()=>true,_=>{});first.FilesList.SelectedIndex=second.FilesList.SelectedIndex=0;
   Require(await first.ConfigureOcrModelsAsync(true,()=>root),"Settlement models explicit installation");SetField(first,"ocrBackend",backend);
   SetField(first,"notice",(Action<string>)(_=>throw new IOException("Synthetic OCR native notice callback")));
   Require(!await first.RecognizeSelectedAsync()&&backend.Started.Task.IsCompleted&&!Field<bool>(first,"busy"),"Synthetic failed result/notice callback returns safely before settlement");
   Require(!await second.PreviewSelectedAsync(),"Other main/sticky image slot refuses while OCR cleanup is unsettled");
   backend.Settled.TrySetResult();await Idle();bool restored=false;for(int i=0;i<100&&!restored;i++){restored=await second.PreviewSelectedAsync();if(!restored)await Task.Delay(10);}
   Require(restored,"Global image slot returns only after successful cleanup settlement");await session.LockAsync();Require(session.KeysReleased&&first.OcrResult.Text==""&&second.PreviewImage.Source is null,"Settlement fixture lock clears all native previews");
  }
  finally{backend.Settled.TrySetResult();CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
 }
}

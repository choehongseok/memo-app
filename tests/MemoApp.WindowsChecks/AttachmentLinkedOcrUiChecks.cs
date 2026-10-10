using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using System.Reflection;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task AttachmentLinkedOcrUiRun()
    {
        Require(typeof(AttachmentPanel).GetMethod("RecognizeLinkedSelectedAsync") is not null&&typeof(AttachmentPanel).GetMethod("ApplyLinkedOcrResultAsync") is not null,"Approved source-linked OCR UI APIs exist");
        string root=Path.Combine(Path.GetTempPath(),"memo-wpf-linked-ocr-"+Guid.NewGuid().ToString("N")),vaultRoot=Path.Combine(root,"vault");Directory.CreateDirectory(root);
        byte[] secret=EncryptedVault.GenerateRecoverySecret(),png=WithScalarPngMetadata(Convert.FromBase64String(File.ReadAllText("tests/fixtures/ocr-synthetic-png.base64")));Window? host=null;
        try
        {
            using(var active=new SaveCoordinator(EncryptedVault.Create(vaultRoot,secret,secret),TimeProvider.System))
            {
                var note=active.Workspace.CreateNote();note.Title="원본 이미지 메모";note.Text="original body retained";active.Workspace.ConvertMode(note,"markdown",true);
                Require(await active.PrepareAttachmentsAsync(),"Linked native fixture current root authenticated");active.AttachBytes(note,png,"합성.png","image/png",note.EditVersion);Require(await active.SaveAsync(),"Linked native source checkpoint");
                var other=active.Workspace.Duplicate(note);other.Title="같은 객체 다른 메모";other.Text="other note body";Require(await active.SaveAsync(),"Shared-object second note clean checkpoint");Guid id=note.AttachmentIds.Single();bool current=true;
                AttachmentPanel? faultPanel=null;bool publicationClearFault=false,publicationPrimeObserved=false;
                Action<NoteDraft?> primeNativeClear=source=>{if(publicationClearFault&&ReferenceEquals(source,note)&&active.Workspace.IsOcrPublicationActive){publicationPrimeObserved=true;faultPanel!.OcrResult.Text="synthetic native reentry text";}};active.Workspace.AttachmentReadInvalidating+=primeNativeClear;
                using var panel=new AttachmentPanel(active,note,()=>current,_=>{});faultPanel=panel;host=new Window{Content=panel,Width=900,Height=850};host.Show();await Idle();panel.FilesList.SelectedIndex=0;
                Require(panel.OcrLinkRecognizeButton.Content.ToString()!.Contains("검색")&&panel.OcrLinkApplyButton.Content.ToString()!.Contains("검색")&&panel.OcrApplyButton.Content.ToString()!.Contains("새 메모"),"Actual native controls distinguish linked search from existing new-note action");
                Require(!await panel.RecognizeLinkedSelectedAsync()&&!await panel.ApplyLinkedOcrResultAsync(),"Linked OCR has no model/start/result implicit fallback");
                Require(await panel.ConfigureOcrModelsAsync(true,()=>root),"Explicit local verified models shared with linked action");
                string baseline=JsonSerializer.Serialize(active.Workspace.Capture());byte[] cipher=File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"));
                try
                {
                    Require(await panel.RecognizeLinkedSelectedAsync()&&OcrComparable(panel.OcrResult.Text)==OcrComparable(OcrExpected)&&panel.OcrLinkApplyButton.IsEnabled&&!panel.OcrResult.CanUndo,"Actual staged linked native recognition publishes genuine bounded read-only result after cleanup");
                    Require(JsonSerializer.Serialize(active.Workspace.Capture())==baseline&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"))),"Linked preview alone preserves complete source/current ciphertext");
                    panel.FilesList.SelectedIndex=-1;panel.FilesList.SelectedIndex=0;Require(panel.OcrResult.Text.Length==0&&!panel.OcrLinkApplyButton.IsEnabled&&!await panel.ApplyLinkedOcrResultAsync(),"Attachment selection away/back immediately clears and monotonically retires linked result");
                    Require(await panel.RecognizeLinkedSelectedAsync(),"Same-version revoke genuine linked candidate ready");active.Workspace.AcceptPrepared(active.Workspace.Capture());Require(panel.OcrResult.Text.Length==0&&!await panel.ApplyLinkedOcrResultAsync(),"Same-version AcceptPrepared immediately clears native linked OCR result");
                    var nativeText=DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty,typeof(TextBox));bool reentered=false;EventHandler selectAgain=(_,_)=>{if(panel.OcrResult.Text.Length>0){reentered=true;panel.FilesList.SelectedIndex=-1;panel.FilesList.SelectedIndex=0;}};nativeText.AddValueChanged(panel.OcrResult,selectAgain);
                    try{Require(!await panel.RecognizeLinkedSelectedAsync()&&reentered&&panel.OcrResult.Text.Length==0&&!panel.OcrLinkApplyButton.IsEnabled,"Native result setter selection reentry publishes no stale result");}
                    finally{nativeText.RemoveValueChanged(panel.OcrResult,selectAgain);}
                    Require(await panel.RecognizeLinkedSelectedAsync(),"Preapply native clear failure genuine linked candidate ready");
                    var preapplyEnabled=DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool preapplyFault=false;EventHandler failClear=(_,_)=>{if(!panel.OcrApplyButton.IsEnabled){preapplyFault=true;throw new IOException("Synthetic preapply native clear refusal");}};preapplyEnabled.AddValueChanged(panel.OcrApplyButton,failClear);
                    try{Require(!await panel.ApplyLinkedOcrResultAsync()&&preapplyFault&&panel.OcrResult.Text.Length==0&&!panel.OcrLinkApplyButton.IsEnabled&&JsonSerializer.Serialize(active.Workspace.Capture())==baseline&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"))),"Native preapply clear refusal drains result and preserves exact whole source/current ciphertext");}
                    finally{preapplyEnabled.RemoveValueChanged(panel.OcrApplyButton,failClear);}
                    Require(await panel.RecognizeLinkedSelectedAsync(),"Own publication invalidation clear failure genuine candidate ready");bool publicationFaultObserved=false;EventHandler failPublicationClear=(_,_)=>{if(publicationClearFault&&active.Workspace.IsOcrPublicationActive&&panel.OcrResult.Text.Length==0){publicationFaultObserved=true;throw new IOException("Synthetic own-publication native clear refusal");}};nativeText.AddValueChanged(panel.OcrResult,failPublicationClear);
                    try{publicationClearFault=true;Require(!await panel.ApplyLinkedOcrResultAsync()&&publicationPrimeObserved&&publicationFaultObserved&&panel.OcrResult.Text.Length==0&&!panel.OcrLinkApplyButton.IsEnabled&&JsonSerializer.Serialize(active.Workspace.Capture())==baseline&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"))),"Native clear failure during exact own Core invalidation retires applying permit before metadata mutation");}
                    finally{publicationClearFault=false;nativeText.RemoveValueChanged(panel.OcrResult,failPublicationClear);active.Workspace.AttachmentReadInvalidating-=primeNativeClear;}
                    Require(await panel.RecognizeLinkedSelectedAsync(),"Publication reentry genuine linked candidate ready");Action<NoteDraft?> duringApply=_=>{panel.FilesList.SelectedIndex=-1;panel.FilesList.SelectedIndex=0;};active.Workspace.AttachmentReadInvalidating+=duringApply;
                    try{Require(!await panel.ApplyLinkedOcrResultAsync()&&note.Metadata.AttachmentOcrResults.IsEmpty&&other.Metadata.AttachmentOcrResults.IsEmpty&&JsonSerializer.Serialize(active.Workspace.Capture())==baseline&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"))),"True selection reentry during Core publication retires exact applying permit before incoming metadata");}
                    finally{active.Workspace.AttachmentReadInvalidating-=duringApply;}
                    Require(await panel.RecognizeLinkedSelectedAsync(),"Host close reentry genuine candidate ready");Action<NoteDraft?> closeHost=_=>panel.Dispose();active.Workspace.AttachmentReadInvalidating+=closeHost;
                    try{Require(!await panel.ApplyLinkedOcrResultAsync()&&panel.IsDisposed&&panel.OcrResult.Text.Length==0&&note.Metadata.AttachmentOcrResults.IsEmpty,"Host disposal during metadata publication retires exact applying permit");}
                    finally{active.Workspace.AttachmentReadInvalidating-=closeHost;}
                }
                finally{CryptographicOperations.ZeroMemory(cipher);}
                host.Close();host=null;
                using var accepted=new AttachmentPanel(active,note,()=>true,_=>{});host=new Window{Content=accepted,Width=900,Height=850};host.Show();await Idle();accepted.FilesList.SelectedIndex=0;
                string models=Directory.EnumerateDirectories(root,"memo-ocr-models-*").Single();Require(await accepted.ConfigureOcrModelsAsync(false,()=>models)&&await accepted.RecognizeLinkedSelectedAsync(),"Fresh host explicitly selects verified models and genuine linked candidate");
                string originalTitle=note.Title,originalText=note.Text,originalMode=note.Mode,otherBefore=JsonSerializer.Serialize(other.Metadata);var attachments=note.AttachmentIds;long version=note.EditVersion;
                Require(await accepted.ApplyLinkedOcrResultAsync(),"Explicit selected-note attachment search metadata encrypted save succeeds");
                Require(note.Title==originalTitle&&note.Text==originalText&&note.Mode==originalMode&&note.AttachmentIds.SequenceEqual(attachments)&&note.EditVersion>version&&otherBefore==JsonSerializer.Serialize(other.Metadata),"Linked apply preserves original title/body/mode/image references and every other note sharing object");
                var stored=note.Metadata.AttachmentOcrResults.Single();Require(stored.ObjectId==id&&OcrComparable(stored.Text)==OcrComparable(OcrExpected)&&stored.Provenance.PpmSha256.Length==64,"Genuine verified linked result carries exact source object and prepared input provenance");
                Require(NoteSearch.Find(active.Workspace,new(){Query="ABC 123",Field=SearchField.Attachments}).SequenceEqual([note])&&NoteSearch.Find(active.Workspace,new(){Query="ABC 123",Field=SearchField.Body}).Length==0,"Linked text searches only selected note attachment field while body remains unchanged");
                Require(accepted.OcrResult.Text.Length==0&&!await accepted.ApplyLinkedOcrResultAsync(),"Linked candidate consumed once with native result clearing");
                // A detached runtime fixture has a synthetic stamp and no publication registry entry.
                // Core retirement must continue to zero every issued publication candidate immediately.
                var item=active.Workspace.AttachmentObject(note,id);var detachedSource=new OcrSourceDescriptor(item.ObjectId,item.RootId,item.Sha256,item.Length);var detachedStamp=new OcrGrantStamp(Guid.NewGuid(),Guid.NewGuid(),note.Id,note.EditVersion,active.AttachmentPreviewEpoch);
                var detachedGrant=AttachmentOcrReadGrant.Issued(active.CreateAttachmentReadLease(note,id,note.EditVersion),detachedSource,detachedStamp);
                using var starter=Field<InstalledOcrBackend>(accepted,"ocrBackend").PrepareLinked(detachedGrant,models,CancellationToken.None);
                bool manualAdmission=false,drainOwnsAdmission=false;OwnedOcrDerivation? late=null;Task? drain=null;
                TaskCompletionSource<OwnedOcrDerivation>? output=null;TaskCompletionSource? settled=null;
                try
                {
                    using var second=new AttachmentPanel(active,other,()=>true,_=>{});second.FilesList.SelectedIndex=0;
                    manualAdmission=ImagePreviewAdmission.TryEnter(accepted.Dispatcher);Require(manualAdmission,"Detached cleanup fixture owns real global image admission");
                    starter.Start();late=await starter.Operation.Completion;await starter.Operation.Settled;var ownedUtf8=Field<byte[]>(late,"utf8");Require(ownedUtf8.Any(b=>b!=0),"Actual detached verified runtime candidate remains owned outside publication registry");
                    output=new(TaskCreationOptions.RunContinuationsAsynchronously);settled=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    drain=(Task)typeof(AttachmentPanel).GetMethod("DrainLinkedResultAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new LinkedOcrOperation(output.Task,settled.Task)])!;drainOwnsAdmission=true;
                    settled.TrySetResult();await Idle();Require(!drain.IsCompleted&&!await second.PreviewSelectedAsync()&&ownedUtf8.Any(b=>b!=0),"Successful native settlement alone does not release admission or lose still-pending candidate ownership");
                    output.TrySetResult(late);await drain;Require(ownedUtf8.All(b=>b==0)&&await second.PreviewSelectedAsync(),"Actual late candidate bytes zero and admission returns only after both detached tasks complete");
                }
                finally
                {
                    try{if(late is not null)output?.TrySetResult(late);else output?.TrySetCanceled();settled?.TrySetResult();if(drain is not null)await drain;}
                    finally
                    {
                        try{starter.Dispose();}catch{}
                        // Always drain the exact real operation, including a refused launch or failed assertion.
                        try{using var actual=await starter.Operation.Completion;}catch{}
                        try{await starter.Operation.Settled;}catch{}
                        late?.Dispose();
                        if(manualAdmission&&!drainOwnsAdmission&&starter.Operation.Completion.IsCompleted&&starter.Operation.Settled.IsCompletedSuccessfully)ImagePreviewAdmission.Exit();
                    }
                }
                accepted.FilesList.SelectedIndex=0;Require(await accepted.RecognizeLinkedSelectedAsync()&&!accepted.OcrLinkApplyButton.IsEnabled,"Duplicate selected-note result has no silent replacement action");Require(await accepted.ApplyOcrResultAsync(),"Existing explicit new-note action may consume genuine linked text as alternative");
                var copied=active.Workspace.Notes.Single(n=>n.Title=="OCR 결과");Require(copied.Mode=="plain"&&copied.Metadata.AttachmentOcrResults.IsEmpty&&OcrComparable(copied.Text)==OcrComparable(OcrExpected),"New-note alternative stays plain independent text without minting linked provenance");
                accepted.FilesList.SelectedIndex=0;Require(await accepted.RecognizeLinkedSelectedAsync(),"Final linked result prepared for independent native lock clearing");
                var enabled=DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool clearFault=false;EventHandler fault=(_,_)=>{if(!accepted.OcrApplyButton.IsEnabled){clearFault=true;throw new IOException("Synthetic linked native disable callback");}};enabled.AddValueChanged(accepted.OcrApplyButton,fault);
                try{await active.LockAsync();Require(clearFault&&active.KeysReleased&&accepted.IsDisposed&&accepted.OcrResult.Text.Length==0&&!accepted.OcrApplyButton.IsEnabled&&!accepted.OcrLinkApplyButton.IsEnabled,"Lock releases keys and clears all linked display even when native disable callback fails");}
                finally{enabled.RemoveValueChanged(accepted.OcrApplyButton,fault);}
                host.Close();host=null;
            }
            using(var reopened=new SaveCoordinator(EncryptedVault.Open(vaultRoot,secret),TimeProvider.System))
            {
                var linked=reopened.Workspace.Notes.Single(n=>!n.Metadata.AttachmentOcrResults.IsEmpty);Require(NoteSearch.Find(reopened.Workspace,new(){Query="ABC 123",Field=SearchField.Attachments}).SequenceEqual([linked])&&linked.Text=="original body retained","Encrypted reopen retains source-linked text and exact original body");
                using var panel=new AttachmentPanel(reopened,linked,()=>true,_=>{});panel.FilesList.SelectedIndex=0;Require(!await panel.RecognizeLinkedSelectedAsync(),"Reopen does not silently activate models or OCR");await reopened.LockAsync();
            }
            var failing=new LinkedOcrSaveFiles();using(var unsaved=new SaveCoordinator(EncryptedVault.Open(vaultRoot,secret,files:failing),TimeProvider.System))
            {
                var note=unsaved.Workspace.Notes.Single(n=>n.Title=="같은 객체 다른 메모");using var panel=new AttachmentPanel(unsaved,note,()=>true,_=>{});host=new Window{Content=panel,Width=900,Height=850};host.Show();await Idle();panel.FilesList.SelectedIndex=0;
                string models=Directory.EnumerateDirectories(root,"memo-ocr-models-*").Single();Require(await panel.ConfigureOcrModelsAsync(false,()=>models)&&await panel.RecognizeLinkedSelectedAsync(),"Save-failure fixture has a genuine verified selected-note candidate");
                byte[] before=File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"));
                try{failing.Fail=true;Require(!await panel.ApplyLinkedOcrResultAsync()&&unsaved.IsDirty&&note.Metadata.AttachmentOcrResults.Length==1&&note.Text=="other note body"&&before.SequenceEqual(File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"))),"Actual flush failure reports retained whole dirty linked metadata and preserves original body/committed ciphertext");}
                finally{CryptographicOperations.ZeroMemory(before);}
                await unsaved.LockAsync();host.Close();host=null;
            }
        }
        finally{host?.Close();CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(png);Directory.Delete(root,true);}
    }
    private sealed class LinkedOcrSaveFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal bool Fail;
        public Stream CreateNew(string path)=>actual.CreateNew(path);
        public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic linked OCR flush refusal");actual.FlushToDisk(stream);}
        public void Move(string temporary,string current)=>actual.Move(temporary,current);
        public void Replace(string temporary,string current,string previous)=>actual.Replace(temporary,current,previous);
    }
}

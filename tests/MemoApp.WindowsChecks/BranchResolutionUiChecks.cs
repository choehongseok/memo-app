using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
 private static async Task BranchResolutionUiRun()
 {
  string root=Path.Combine(Path.GetTempPath(),"memo-wpf-branch-resolution-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;Task<bool>? pending=null;PausedBatchFiles? paused=null;
  try
  {
   var now=DateTimeOffset.UnixEpoch;var a=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"공통 A","ancestor");
   var b=a with{RevisionId=Guid.NewGuid(),Parents=[a.RevisionId],Title="현재 B",Text="# exact current body",Mode="markdown"};
   var c=new StoredRevision(a.NoteId,Guid.NewGuid(),[a.RevisionId],now,"보존 C","incoming C");var d=c with{RevisionId=Guid.NewGuid(),Title="보존 D",Text="incoming D"};
   var other=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,"다른 메모","other exact body");
   var seed=new VaultSnapshot(4,Guid.NewGuid(),[b,other]){History=[new(a.NoteId,a.RevisionId,[],now,a.Title,a.Text),c,d]};using(var vault=EncryptedVault.Create(root,secret,secret))vault.Save(seed);
   main=new MainWindow(root);SetField(main,"searchBlocked",true);main.Show();Invoke(main,"StartSession",EncryptedVault.Open(root,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
   var note=active.Workspace.Notes.Single(n=>n.Id==a.NoteId);var another=active.Workspace.Notes.Single(n=>n.Id==other.NoteId);var list=Control<ListBox>(main,"NotesList");
   async Task Populate(){SetField(main,"searchBlocked",false);Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");await Idle();list.UnselectAll();list.SelectedItem=note;SetField(main,"searchBlocked",true);Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItem,note),"Resolution actual native list has exact target selection");}
   await Populate();Require(await active.SaveAsync(),"Native resolution initial UI checkpoint");
   var open=typeof(MainWindow).GetMethod("ShowPendingBackupBranchesAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
   var apply=typeof(MainWindow).GetMethod("ConfirmBranchResolutionAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
   Task<bool> Open()=>(Task<bool>)open.Invoke(main,[])!;
   Task<bool> Apply(BackupMergeWindow viewer,Func<bool> confirm,IAtomicVaultFiles? files=null)=>(Task<bool>)apply.Invoke(main,[viewer,confirm,files])!;
   BackupMergeWindow Viewer()=>Field<HashSet<BackupMergeWindow>>(main,"backupMergeWindows").Single();
   async Task Select(BackupMergeWindow viewer,int index){viewer.BranchesList.SelectedIndex=index;await viewer.WhenResolutionSelectionIdle;}
   void RoundTrip(){list.SelectedItem=another;list.SelectedItem=note;}
   note.Text="# exact dirty current body";Require(await Open()&&!active.IsDirty,"Opening durable comparison separately saves exact dirty current edit");var viewer=Viewer();
   Require(viewer.BranchesList.SelectedIndex==-1&&viewer.CurrentTextView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0&&!viewer.ResolveButton.IsEnabled&&!viewer.PreserveButton.IsEnabled,"Durable view initially has no selected tip or displayed/confirmation authority");
   Require(!await Apply(viewer,()=>throw new Exception("No confirmation before explicit displayed tip")),"No native confirmation callback before genuine selected display");
   byte[] baseline=SnapshotSerialization.Bytes(active.Workspace.Capture()),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
   try
   {
    await Select(viewer,1);var chosen=(BackupMergeBranchComparison)viewer.BranchesList.SelectedItem;Guid tip=chosen.Incoming.RevisionId;Require(viewer.ResolveButton.IsEnabled&&viewer.CurrentTextView.Text==note.Text&&viewer.IncomingTextView.Text==chosen.Incoming.TextExcerpt,"Explicit non-first maximal pending tip publishes exact independent native comparison");
    Require(!await Apply(viewer,()=>false)&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Default-No leaves exact current/history/ciphertext unchanged");
    viewer.BranchesList.SelectedIndex=-1;await viewer.WhenResolutionSelectionIdle;Require(!viewer.ResolveButton.IsEnabled&&!await Apply(viewer,()=>throw new Exception("No old selected token")),"Changing tip selection retires prior displayed authority immediately");await Select(viewer,1);
    Require(!await Apply(viewer,()=>{viewer.BranchesList.SelectedIndex=-1;viewer.BranchesList.SelectedIndex=1;return true;})&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Tip selection away/back during confirmation never adopts replacement token");await viewer.WhenResolutionSelectionIdle;viewer.Dispose();
    Require(await Open(),"Native target-selection revoke viewer opens");viewer=Viewer();await Select(viewer,1);RoundTrip();Require(viewer.IsRevoked&&viewer.IncomingTextView.Text.Length==0&&!await Apply(viewer,()=>throw new Exception("No main selection stale confirm")),"Main target selection away/back closes and independently clears selected branch authority");
    Require(await Open(),"Native same-version viewer opens");viewer=Viewer();await Select(viewer,1);active.Workspace.AcceptPrepared(active.Workspace.Capture());Require(viewer.IsRevoked&&!await Apply(viewer,()=>throw new Exception("No accepted-basis stale confirm")),"Same-version AcceptPrepared immediately revokes and clears resolution");
    Require(await Open(),"Native host-close confirmation viewer opens");viewer=Viewer();await Select(viewer,1);Require(!await Apply(viewer,()=>{viewer.Close();return true;})&&viewer.IsRevoked&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Closing selected native host during confirmation revokes exact displayed token");
    Require(await Open(),"Native hide confirmation viewer opens");viewer=Viewer();await Select(viewer,1);Require(!await Apply(viewer,()=>{viewer.Hide();return true;})&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Hiding the selected comparison during confirmation revokes visible display authority without causal publication");viewer.Dispose();
    Require(await Open(),"Native ready-button hide viewer opens");viewer=Viewer();var readyDescriptor=DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty,typeof(Button));bool readyHidden=false;EventHandler hideReady=(_,_)=>{if(viewer.ResolveButton.IsEnabled){readyHidden=true;viewer.Hide();}};readyDescriptor.AddValueChanged(viewer.ResolveButton,hideReady);
    try{await Select(viewer,1);Require(readyHidden&&viewer.IsRevoked&&!await Apply(viewer,()=>throw new Exception("Hidden setter cannot confirm"))&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Native ready setter hide fails post-display guard, closes host and preserves complete candidate/ciphertext");}
    finally{readyDescriptor.RemoveValueChanged(viewer.ResolveButton,hideReady);viewer.Dispose();}
    Require(await Open(),"Nested recovery admission viewer opens");viewer=Viewer();await Select(viewer,1);Require(!await Apply(viewer,()=>{Require(!Open().GetAwaiter().GetResult(),"Nested native recovery preview is refused by shared admission");return false;})&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Nested confirmation callback cannot replace source authority");viewer.Dispose();
    Require(await Open(),"Native confirmation edit viewer opens");viewer=Viewer();await Select(viewer,1);Require(!await Apply(viewer,()=>{note.Text+="\nexplicit reentrant edit";return true;})&&note.Text.EndsWith("explicit reentrant edit",StringComparison.Ordinal)&&active.Workspace.Capture().History.All(h=>h.RevisionId!=Guid.Empty),"Confirmation edit preserves its exact current edit and applies no causal event");note.Text="# exact dirty current body";Require(await active.SaveAsync(),"Explicit reentrant edit checkpoint restored fixture body");
    CryptographicOperations.ZeroMemory(baseline);baseline=SnapshotSerialization.Bytes(active.Workspace.Capture());CryptographicOperations.ZeroMemory(cipher);cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
    foreach(string field in new[]{nameof(BackupMergeWindow.CurrentTitleView),nameof(BackupMergeWindow.CurrentTextView),nameof(BackupMergeWindow.IncomingTitleView),nameof(BackupMergeWindow.IncomingTextView)})
    {
     Require(await Open(),"Native setter refusal viewer opens: "+field);viewer=Viewer();var target=(TextBox)typeof(BackupMergeWindow).GetProperty(field)!.GetValue(viewer)!;var property=DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty,typeof(TextBox));bool threw=false;EventHandler fail=(_,_)=>{if(target.Text.Length>0){threw=true;throw new IOException("Synthetic selected comparison setter refusal");}};property.AddValueChanged(target,fail);
     try{await Select(viewer,1);Require(threw&&viewer.IsRevoked&&viewer.CurrentTitleView.Text.Length==0&&viewer.CurrentTextView.Text.Length==0&&viewer.IncomingTitleView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0&&!await Apply(viewer,()=>throw new Exception("Failed native presentation cannot confirm")),"Every selected native field refusal closes/clears independently without display grant: "+field);}
     finally{property.RemoveValueChanged(target,fail);viewer.Dispose();}
    }
    Require(await Open(),"Native display selection reentry viewer opens");viewer=Viewer();var descriptor=DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty,typeof(TextBox));EventHandler reenter=(_,_)=>{if(viewer.IncomingTitleView.Text.Length>0)RoundTrip();};descriptor.AddValueChanged(viewer.IncomingTitleView,reenter);
    try{await Select(viewer,1);Require(viewer.IsRevoked&&viewer.IncomingTitleView.Text.Length==0&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture())),"Native selected display reentry publishes no stale token or model change");}
    finally{descriptor.RemoveValueChanged(viewer.IncomingTitleView,reenter);viewer.Dispose();}
    Require(await Open(),"Paused copy resolution viewer opens");viewer=Viewer();await Select(viewer,1);paused=new();pending=Apply(viewer,()=>true,paused);
    try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));RoundTrip();paused.Continue.TrySetResult();Require(!await pending&&viewer.IsRevoked&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Selection during exact encrypted preservation refuses publication and retains whole current state");}
    finally{paused.Continue.TrySetResult();await pending;pending=null;paused=null;}
    Require(await Open(),"Current-cipher replacement preservation viewer opens");viewer=Viewer();await Select(viewer,1);paused=new();pending=Apply(viewer,()=>true,paused);
    try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));File.WriteAllBytes(Path.Combine(root,"current.vault"),[1,2,3]);paused.Continue.TrySetResult();Require(!await pending&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(new byte[]{1,2,3}),"Current-cipher replacement after prechange capture is refused without overwrite or causal publication");}
    finally{paused.Continue.TrySetResult();await pending;File.WriteAllBytes(Path.Combine(root,"current.vault"),cipher);pending=null;paused=null;}
    BackupMergeWindow? clearing=null;bool primeRan=false;Action<NoteDraft?> prime=source=>{if(ReferenceEquals(source,note)&&active.Workspace.IsBranchResolutionPublicationActive){primeRan=true;clearing!.CurrentTextView.Text="synthetic clear-boundary reentry";}};active.Workspace.AttachmentReadInvalidating+=prime;
    Require(await Open(),"Own publication clear-failure viewer opens");viewer=Viewer();clearing=viewer;await Select(viewer,1);bool clearFailure=false;EventHandler clearFault=(_,_)=>{if(active.Workspace.IsBranchResolutionPublicationActive&&viewer.CurrentTextView.Text.Length==0){clearFailure=true;throw new IOException("Synthetic causal publication clear refusal");}};descriptor.AddValueChanged(viewer.CurrentTextView,clearFault);
    try{Require(!await Apply(viewer,()=>true)&&primeRan&&clearFailure&&viewer.IsRevoked&&viewer.CurrentTextView.Text.Length==0&&viewer.IncomingTextView.Text.Length==0&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Own causal publication native clear refusal retires exact permit before field installation");}
    finally{active.Workspace.AttachmentReadInvalidating-=prime;descriptor.RemoveValueChanged(viewer.CurrentTextView,clearFault);viewer.Dispose();}
    Require(await Open(),"Native publication reentry viewer opens");viewer=Viewer();await Select(viewer,1);Action<NoteDraft?> invalidating=source=>{if(ReferenceEquals(source,note)&&active.Workspace.IsBranchResolutionPublicationActive)RoundTrip();};active.Workspace.AttachmentReadInvalidating+=invalidating;
    try{Require(!await Apply(viewer,()=>true)&&baseline.SequenceEqual(SnapshotSerialization.Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"True selection reentry at causal publication retires exact applying authority");}
    finally{active.Workspace.AttachmentReadInvalidating-=invalidating;}
    Require(await Open(),"Successful explicit causal resolution viewer opens");viewer=Viewer();await Select(viewer,1);chosen=(BackupMergeBranchComparison)viewer.BranchesList.SelectedItem;tip=chosen.Incoming.RevisionId;var before=active.Workspace.Capture();var old=before.Notes.Single(n=>n.NoteId==note.Id);var untouched=before.Notes.Single(n=>n.NoteId==another.Id);var recovery=new RecordingMergeFiles();
    Require(await Apply(viewer,()=>true,recovery),"Actual default-No-capable explicit selected tip resolution durably saves");var after=active.Workspace.Capture();var head=after.Notes.Single(n=>n.NoteId==note.Id);
    Require(head.Parents.SequenceEqual(new[]{old.RevisionId,tip})&&head.RevisionId!=old.RevisionId&&head.Title==old.Title&&head.Text==old.Text&&head.Mode==old.Mode&&JsonSerializer.Serialize(head.Metadata)==JsonSerializer.Serialize(old.Metadata)&&head.Document==old.Document&&head.AttachmentIds.SequenceEqual(old.AttachmentIds)&&JsonSerializer.Serialize(untouched)==JsonSerializer.Serialize(after.Notes.Single(n=>n.NoteId==another.Id))&&after.History.Any(h=>h.RevisionId==old.RevisionId&&h.Text==old.Text)&&after.History.Any(h=>h.RevisionId==tip),"Native resolution keeps exact current content and other note, ordered two parents and both immutable predecessor records");
    Require(recovery.Destination is{ } destination&&cipher.SequenceEqual(File.ReadAllBytes(destination)),"Native resolution preserves exact clean encrypted prechange copy");
    Require(await Open(),"Post-resolution durable pending comparison opens");viewer=Viewer();Require(viewer.BranchesList.SelectedIndex==-1&&viewer.BranchesList.Items.Count==1&&viewer.BranchesList.Items.Cast<BackupMergeBranchComparison>().All(x=>x.Incoming.RevisionId!=tip),"Selected pending tip removed while other maximal tip remains with no automatic selection");viewer.Dispose();
   }
   finally{CryptographicOperations.ZeroMemory(baseline);CryptographicOperations.ZeroMemory(cipher);}
   await active.LockAsync();Invoke(main,"ReleaseSettledSession");Invoke(main,"StartSession",EncryptedVault.Open(root,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();note=active.Workspace.Notes.Single(n=>n.Id==a.NoteId);another=active.Workspace.Notes.Single(n=>n.Id==other.NoteId);await Populate();
   Require(await Open(),"Actual encrypted reopen retains unresolved other branch");var reopened=Viewer();Require(reopened.BranchesList.Items.Count==1&&reopened.BranchesList.SelectedIndex==-1,"Encrypted restart retains chosen ancestry and unrelated pending tip");await Select(reopened,0);
   paused=new();pending=Apply(reopened,()=>true,paused);
   try{await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!pending.IsCompleted&&reopened.IsRevoked&&reopened.CurrentTextView.Text.Length==0&&reopened.IncomingTextView.Text.Length==0,"Lock clears native resolution and releases keys while cipher-only preservation worker remains owned");paused.Continue.TrySetResult();Require(!await pending,"Locked paused resolution applies no causal event");await locking;}
   finally{paused.Continue.TrySetResult();await pending;pending=null;paused=null;}
   Invoke(main,"ReleaseSettledSession");var files=new BranchResolutionSaveFiles();Invoke(main,"StartSession",EncryptedVault.Open(root,secret,files:files));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();note=active.Workspace.Notes.Single(n=>n.Id==a.NoteId);another=active.Workspace.Notes.Single(n=>n.Id==other.NoteId);await Populate();Require(await active.SaveAsync(),"AppliedDirty synthetic UI clean checkpoint");Require(await Open(),"AppliedDirty selected resolution native preview");var dirty=Viewer();await Select(dirty,0);byte[] committed=File.ReadAllBytes(Path.Combine(root,"current.vault"));
   try{files.Fail=true;Require(!await Apply(dirty,()=>true)&&active.IsDirty&&Control<TextBlock>(main,"Notice").Text.Contains("전체 변경을 유지")&&active.Workspace.PendingBackupBranchTips(note).Length==0&&committed.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Actual save failure reports AppliedDirty, retains whole causal candidate and exact committed ciphertext");}
   finally{files.Fail=false;CryptographicOperations.ZeroMemory(committed);}
   var retained=active.Workspace.Capture();Guid retainedHead=retained.Notes.Single(n=>n.NoteId==note.Id).RevisionId;int retainedHistory=retained.History.Length;
   Require(await active.SaveAsync()&&active.Workspace.Capture().Notes.Single(n=>n.NoteId==note.Id).RevisionId==retainedHead&&active.Workspace.Capture().History.Length==retainedHistory,"Explicit normal save retries the retained candidate without a second causal revision or duplicated predecessor");await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();main=null;
  }
  finally
  {
   bool workerSettled=pending is null,sessionReleased=main is null;
   try
   {
    paused?.Continue.TrySetResult();
    try{if(pending is not null)await pending;}
    finally
    {
     workerSettled=pending is null||pending.IsCompleted;
     if(main is not null)
     {
      var session=Field<SaveCoordinator?>(main,"session");
      try{if(session is not null)await session.LockAsync();}
      finally
      {
       if(session is null||session.IsLocked&&session.KeysReleased&&!session.IsBusy)
       {
        // Synthetic failure cleanup bypasses the product's interactive pending-recovery prompt.
        // Discard only after real writer settlement and key release; a failed Dispose retains the directory.
        session?.Dispose();SetField(main,"session",null!);sessionReleased=true;
        if(sessionReleased){SetField(main,"confirmedExit",true);main.Close();}
       }
      }
     }
    }
   }
   finally{CryptographicOperations.ZeroMemory(secret);if(workerSettled&&sessionReleased)Directory.Delete(root,true);}
  }
 }
 private sealed class BranchResolutionSaveFiles:IAtomicVaultFiles
 {
  private readonly AtomicVaultFiles actual=new();internal bool Fail;
  public Stream CreateNew(string path)=>actual.CreateNew(path);
  public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic causal resolution save failure");actual.FlushToDisk(stream);}
  public void Move(string temporary,string current)=>actual.Move(temporary,current);
  public void Replace(string temporary,string current,string previous)=>actual.Replace(temporary,current,previous);
 }
}

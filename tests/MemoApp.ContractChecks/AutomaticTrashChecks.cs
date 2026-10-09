using System.Reflection;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Core;
internal static class AutomaticTrashChecks
{
 internal static void Run()
 {
  void Reject(Action action,string message){bool rejected=false;try{action();}catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException or AggregateException){rejected=true;}VaultChecks.Require(rejected,message);}
  var eligible=typeof(EditingWorkspace).GetMethod("EligibleTrash",BindingFlags.Instance|BindingFlags.NonPublic);var purge=typeof(EditingWorkspace).GetMethod("PurgeTrash",BindingFlags.Instance|BindingFlags.NonPublic);
  VaultChecks.Require(eligible is not null&&purge is not null,"Automatic trash candidate/purge contract missing");
  var cutoff=DateTimeOffset.UtcNow.AddDays(-30);Guid root=Guid.NewGuid(),device=Guid.NewGuid();
  StoredNote Note(bool deleted,DateTimeOffset time)=>new(Guid.NewGuid(),Guid.NewGuid(),[],time,time,"synthetic","retained private synthetic"){Metadata=new(){Deleted=deleted}};
  var old=Note(true,cutoff.AddTicks(-1));var older=Note(true,cutoff.AddDays(-3));var edge=Note(true,cutoff);var future=Note(true,cutoff.AddDays(31));var live=Note(false,cutoff.AddDays(-2));var profile=Guid.NewGuid();
  var snapshot=new VaultSnapshot(7,device,[old,older,edge,future,live]){AttachmentRootId=root,Tombstones=[new(older.NoteId,older.RevisionId,[]),new(old.NoteId,old.RevisionId,[]),new(edge.NoteId,edge.RevisionId,[]),new(future.NoteId,future.RevisionId,[])],UiDevices=[new(profile,new(),[new("memo",old.NoteId,"synthetic",0,0,300,300,96)]){RecentNoteIds=[old.NoteId,live.NoteId]}]};
  var workspace=new EditingWorkspace(TimeProvider.System,snapshot);var retained=workspace.Notes.Single(n=>n.Id==old.NoteId);var retainedOther=workspace.Notes.Single(n=>n.Id==older.NoteId);Guid[] Expected()=> (Guid[])eligible!.Invoke(workspace,[cutoff])!;
  VaultChecks.Require(Expected().SequenceEqual(new[]{old.NoteId,older.NoteId}.Order()),"Strict older-than cutoff; equal boundary/future/live excluded");
  Reject(()=>purge!.Invoke(workspace,[new[]{old.NoteId,old.NoteId},cutoff]),"Duplicate selection refuses before mutation");
  Reject(()=>purge!.Invoke(workspace,[new[]{edge.NoteId},cutoff]),"Changed exact eligibility refuses before mutation");
  VaultChecks.Require(workspace.Capture().Notes.Length==5&&!retained.IsClosed,"Failed preflight leaves draft intact");
  bool dirty=false;workspace.Changed+=()=>throw new InvalidOperationException("synthetic observer");workspace.Changed+=()=>dirty=true;
  retained.PropertyChanged+=(_,_)=>throw new InvalidOperationException("synthetic native close");
  Reject(()=>purge!.Invoke(workspace,[Expected(),cutoff]),"Notification exception reported after applied purge");
  var after=workspace.Capture();VaultEnvelope.Validate(after);
  VaultChecks.Require(dirty&&retainedOther.IsClosed&&retainedOther.Text==""&&retained.IsClosed&&retained.Title==""&&retained.Text==""&&!after.Notes.Any(n=>n.NoteId==old.NoteId)&&after.History.All(n=>n.NoteId!=old.NoteId),"Applied purge clears held draft and dirties despite observer failures");
  VaultChecks.Require(after.SchemaVersion==8&&after.Tombstones.Single(n=>n.NoteId==old.NoteId).RevisionId==old.RevisionId&&after.DiscardedRevisions.Single(w=>w.NoteId==old.NoteId).RevisionId==old.RevisionId,"Original deletion identity retained without content");
  VaultChecks.Require(after.Notes.Single(n=>n.NoteId==live.NoteId)==live&&after.UiDevices.Single().Windows.Length==0&&after.UiDevices.Single().RecentNoteIds.SequenceEqual(new[]{live.NoteId}),"Active note unchanged; all removed profile references cleared");
  VaultChecks.Require(((Guid[])purge!.Invoke(workspace,[Array.Empty<Guid>(),cutoff])!).Length==0,"Empty exact candidate noop");
  Console.WriteLine("PASS: automatic trash strict cutoff, stale/duplicate preflight, atomic metadata removal and observer-safe held-draft clear");
 }
}

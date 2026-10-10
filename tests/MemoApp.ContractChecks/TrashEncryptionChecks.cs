using System.Security.Cryptography;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
namespace MemoApp.Core;
internal static class TrashEncryptionChecks
{
 internal static async Task Run()
 {
  string root=Path.Combine(Path.GetTempPath(),"memo-trash-schema8-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();Guid discardedId,activeId;byte[] original;
  try
  {
   using(var vault=EncryptedVault.Create(Path.Combine(root,"vault"),secret,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
   {
    var gone=session.Workspace.CreateNote();gone.Title="discarded synthetic";gone.Text="private synthetic original";var active=session.Workspace.CreateNote();active.Text="active synthetic preserved";discardedId=gone.Id;activeId=active.Id;
    VaultChecks.Require(await session.PrepareAttachmentsAsync(),"Schema8 owns actual anchored attachment root");session.AttachBytes(gone,new byte[]{1,2,3,255},"retained.bin","application/octet-stream",gone.EditVersion);VaultChecks.Require(await session.SaveAsync(),"Actual root5 source accepted");
    session.Workspace.DeleteNote(gone);VaultChecks.Require(await session.SaveAsync(),"Original delete revision accepted before contentless transition");var legacyMarker=new StoredTombstone(Guid.NewGuid(),Guid.NewGuid(),[]);var withLegacy=session.Workspace.Capture() with{Tombstones=session.Workspace.Capture().Tombstones.Append(legacyMarker).ToArray()};vault.Save(withLegacy);session.Workspace.AcceptPrepared(withLegacy);VaultChecks.ExpectFailure(()=>vault.Prepare(withLegacy with{SchemaVersion=8,Tombstones=withLegacy.Tombstones.Where(t=>t.NoteId!=legacyMarker.NoteId).ToArray()}),"Actual first8 encryption refuses lost legacy marker");var before=session.Workspace.Capture();original=File.ReadAllBytes(Path.Combine(root,"vault","current.vault"));
    VaultChecks.Require(await session.BackupAsync(Path.Combine(root,"before.vault")),"Separate actual original encrypted backup");
    var deleted=before.Notes.Single(n=>n.NoteId==discardedId);var witnesses=before.History.Where(h=>h.NoteId==discardedId).Select(h=>new StoredDiscardedRevision(h.NoteId,h.RevisionId,(Guid[])h.Parents.Clone())).Append(new(discardedId,deleted.RevisionId,(Guid[])deleted.Parents.Clone())).ToArray();
    session.Workspace.PurgeTrash([discardedId],DateTimeOffset.UtcNow.AddMinutes(1));var eight=session.Workspace.Capture();
    VaultEnvelope.Validate(eight);VaultChecks.Require(eight.DiscardedRevisions.Select(w=>w.RevisionId).Order().SequenceEqual(witnesses.Select(w=>w.RevisionId).Order())&&gone.IsClosed&&eight.AttachmentObjects.SequenceEqual(before.AttachmentObjects)&&await session.SaveAsync(),"Actual attached-note PurgeTrash closes original, retains exact opaque objects/root and encrypts causal witnesses");
    VaultChecks.Require(Directory.GetFiles(Path.Combine(root,"vault"),"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(original)),"Actual pre8 encrypted bytes retained unchanged");
    VaultChecks.ExpectFailure(()=>vault.Prepare(before with{SchemaVersion=7}),"Prepared8 refuses witness-losing schema7 downgrade");VaultChecks.ExpectFailure(()=>vault.InitializeAttachmentRoot(before),"Root initialization cannot discard prepared8 witnesses");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(before),"Hidden recovery cannot discard prepared8 witnesses");
   }
   using(var vault=EncryptedVault.Open(Path.Combine(root,"vault"),secret))
   {
    var saved=vault.Loaded;VaultChecks.Require(saved.SchemaVersion==8&&saved.Notes.Single().NoteId==activeId&&saved.DiscardedRevisions.All(r=>r.NoteId==discardedId)&&saved.DiscardedRevisions.Length>1&&saved.AttachmentObjects.Length==1,"Actual encrypted8 restart preserves causal deletion and opaque object without original content/history");
    VaultChecks.Require(saved.Tombstones.Single(t=>t.NoteId==discardedId).Parents.SequenceEqual(saved.DiscardedRevisions.Single(w=>w.RevisionId==saved.Tombstones.Single(t=>t.NoteId==discardedId).RevisionId).Parents),"Original deletion parents retained under real AEAD");
    var wraps=typeof(EncryptedVault).GetField("wraps",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;ulong count=(ulong)wraps.GetValue(vault)!;vault.Save(saved);VaultChecks.Require((ulong)wraps.GetValue(vault)! ==count+3,"Schema8 reserves three root envelope wraps");
    VaultChecks.ExpectFailure(()=>vault.Prepare(saved with{DiscardedRevisions=[],Tombstones=[]}),"Same schema8 cannot silently lose causal deletion evidence");
    VaultChecks.ExpectFailure(()=>vault.Prepare(saved with{SchemaVersion=7,DiscardedRevisions=[]}),"Loaded8 refuses empty-field downgrade7");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(saved with{SchemaVersion=12}),"Unknown hidden future12 refused");
    using var session=new SaveCoordinator(vault,TimeProvider.System);var captured=session.Workspace.Capture();VaultChecks.Require(captured.SchemaVersion==8&&captured.DiscardedRevisions.Length==saved.DiscardedRevisions.Length,"Reopened workspace cannot drop encrypted8 deletion witnesses");
    var live=session.Workspace.Notes.Single();live.Title="schema8 active edit";VaultChecks.Require(await session.SaveAsync(),"Actual workspace8 edit/save retains witnesses");
    Guid profile=Guid.NewGuid();session.Workspace.RecordRecentNote(profile,live);session.Workspace.SaveSearch(profile,"eight search",new(){Query="active"});
    string external=Path.Combine(root,"synthetic-existing.txt");File.WriteAllText(external,"public source");session.Workspace.AddFilePathLink(live,FilePathLink.Create(external,profile));VaultChecks.Require(session.Workspace.Capture().SchemaVersion==8&&await session.SaveAsync(),"Path/search metadata after8 cannot force downgrade7");
    string copy=Path.Combine(root,"eight-copy.vault");VaultChecks.Require(await session.BackupAsync(copy),"Actual full encrypted8 backup");byte[] backup=File.ReadAllBytes(copy);
    try{var fresh=session.ImportSelectedEncryptedBackup(backup,[live.Id],session.AttachmentPreviewEpoch).Single();VaultChecks.Require(fresh.Id!=live.Id&&fresh.Id!=discardedId&&session.Workspace.Capture().SchemaVersion==8&&await session.SaveAsync(),"Selected8 backup restores fresh copy without old deletion resurrection or metadata downgrade");}finally{CryptographicOperations.ZeroMemory(backup);}
    var full=session.Workspace.Capture();VaultChecks.Require(full.DiscardedRevisions.Length==saved.DiscardedRevisions.Length&&full.Tombstones.Single(t=>t.NoteId==discardedId).RevisionId==saved.Tombstones.Single(t=>t.NoteId==discardedId).RevisionId,"Live mutations and fresh-copy backup preserve exact original deletion evidence");

   }
   string wholeRoot=Path.Combine(root,"eight-whole");string wholeCandidate=EncryptedVault.ImportEncryptedCopy(wholeRoot,Path.Combine(root,"eight-copy.vault"),secret);using(var transferred=EncryptedVault.Open(wholeRoot,secret,wholeCandidate)){VaultChecks.Require(transferred.Loaded.SchemaVersion==8&&transferred.Loaded.DiscardedRevisions.All(w=>w.NoteId==discardedId)&&transferred.Loaded.Tombstones.Length==2,"Fresh root full8 encrypted transfer preserves prior contentless marker and causal witnesses");}
   var failedFiles=new TrashFaultFiles();Guid failedId;string[] priorPending=[];
   using(var vault=EncryptedVault.Open(wholeRoot,secret,wholeCandidate,failedFiles))using(var failed=new SaveCoordinator(vault,TimeProvider.System))
   {
    var draft=failed.Workspace.CreateNote();draft.Text="synthetic failed purge restore";failedId=draft.Id;failed.Workspace.DeleteNote(draft);VaultChecks.Require(await failed.SaveAsync(),"Failure fixture committed before removal");byte[] prior=File.ReadAllBytes(Path.Combine(wholeRoot,"current.vault"));string safe=Path.Combine(root,"failed-before.vault");VaultChecks.Require(await failed.BackupAsync(safe),"Actual encrypted copy before failing purge write");failed.Workspace.PurgeTrash([failedId],DateTimeOffset.UtcNow.AddMinutes(1));priorPending=Directory.GetFiles(wholeRoot,"pending-*.vault");failedFiles.Fail=true;
    VaultChecks.Require(!await failed.SaveAsync()&&failed.IsDirty&&draft.IsClosed&&failed.Workspace.Capture().Notes.All(n=>n.NoteId!=failedId)&&prior.SequenceEqual(File.ReadAllBytes(Path.Combine(wholeRoot,"current.vault")))&&File.Exists(safe),"Applied8 purge flush fault retains whole dirty state, exact source ciphertext and safe backup");VaultChecks.Require(!await failed.LockAsync()&&failed.KeysReleased,"Failed prepared8 ciphertext retains pending recovery after keys released");
   }
   string failedPending=Directory.GetFiles(wholeRoot,"pending-*.vault").Except(priorPending).Single();using(var recovery=EncryptedVault.Open(wholeRoot,secret,Path.GetFileName(failedPending))){VaultChecks.Require(recovery.Loaded.SchemaVersion==8&&recovery.Loaded.Notes.All(n=>n.NoteId!=failedId)&&recovery.Loaded.DiscardedRevisions.Any(w=>w.NoteId==failedId),"Actual pending8 candidate authenticates applied purge after flush fault");recovery.Save(recovery.Loaded);}
   using(var vault=EncryptedVault.Open(Path.Combine(root,"vault"),secret))using(var hidden=new SaveCoordinator(vault,TimeProvider.System))
   {
    var draft=hidden.Workspace.Notes.First();draft.Title="hidden eight latest";var wraps=typeof(EncryptedVault).GetField("wraps",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;ulong budget=(ulong)wraps.GetValue(vault)!;wraps.SetValue(vault,VaultEnvelope.MaxWraps);
    VaultChecks.Require(!await hidden.LockAsync()&&hidden.PendingKind=="plaintext-hidden"&&!hidden.KeysReleased&&draft.IsClosed&&draft.Text.Length==0,"Actual8 pre-encryption failure conceals drafts and retains explicit hidden recovery");hidden.ResumeHidden(secret);VaultChecks.Require(hidden.Workspace.Capture().SchemaVersion==8&&hidden.Workspace.Capture().Tombstones.Length==2&&hidden.Workspace.Capture().DiscardedRevisions.Length>1&&hidden.Workspace.Notes.First().Title=="hidden eight latest","Actual8 hidden resume preserves causal state and latest unsaved content");wraps.SetValue(vault,budget);VaultChecks.Require(await hidden.SaveAsync()&&await hidden.LockAsync()&&hidden.KeysReleased,"Resumed8 correction actual encryption settles and releases keys");
   }
   string restored=Path.Combine(root,"restore");string pending=EncryptedVault.ImportEncryptedCopy(restored,Path.Combine(root,"before.vault"),secret);using(var recovered=EncryptedVault.Open(restored,secret,pending)){VaultChecks.Require(recovered.Loaded.Notes.Any(n=>n.NoteId==discardedId&&n.Text=="private synthetic original")&&recovered.Loaded.History.Any(h=>h.NoteId==discardedId),"Independent original backup retains full deleted body/history for recovery");}
   Console.WriteLine("PASS: actual root envelope schema8 save/reopen, three-wrap accounting, original ciphertext/backup/history/object preservation and loaded/prepared/hidden downgrade refusal");
  }
  finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
 }
 private sealed class TrashFaultFiles:IAtomicVaultFiles
 {
  private readonly AtomicVaultFiles actual=new();internal bool Fail;
  public Stream CreateNew(string path)=>actual.CreateNew(path);public void FlushToDisk(Stream stream){if(Fail)throw new IOException("Synthetic8 flush failure");actual.FlushToDisk(stream);}public void Move(string a,string b)=>actual.Move(a,b);public void Replace(string a,string b,string c)=>actual.Replace(a,b,c);
 }
}

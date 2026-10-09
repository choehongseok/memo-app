using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Editing;
using MemoApp.Core.Search;
using MemoApp.Core.Storage;
internal static class DeviceSearchActivationChecks
{
    internal static async Task Run()
    {
        var recent=typeof(EditingWorkspace).GetMethod("RecordRecentNote")??throw new Exception("Atomic recent-note metadata activation is missing");var save=typeof(EditingWorkspace).GetMethod("SaveSearch")??throw new Exception("Atomic saved-search metadata activation is missing");var remove=typeof(EditingWorkspace).GetMethod("RemoveSavedSearch")!;var clear=typeof(EditingWorkspace).GetMethod("ClearRecentNotes")!;
        void Recent(EditingWorkspace workspace,Guid profile,NoteDraft note)=>recent.Invoke(workspace,[profile,note]);StoredSavedSearch Save(EditingWorkspace workspace,Guid profile,string name,SearchOptions options)=>(StoredSavedSearch)save.Invoke(workspace,[profile,name,options])!;
        FailureInvariance();PayloadFailure();
        string root=Path.Combine(Path.GetTempPath(),"memo-search-activation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=EncryptedVault.GenerateRecoverySecret();Guid profile=Guid.NewGuid(),other=Guid.NewGuid();string vaultRoot=Path.Combine(root,"vault"),backup=Path.Combine(root,"copy.vault");
        try
        {
            using(var session=new SaveCoordinator(EncryptedVault.Create(vaultRoot,secret,secret),TimeProvider.System))
            {
                var first=session.Workspace.CreateNote();first.Title="합성 최근";first.Text="RECENT_SAVED_QUERY_SYNTHETIC";var second=session.Workspace.CreateNote();second.Text="second";Require(await session.PrepareAttachmentsAsync(),"Root5 baseline anchored");Guid attached=session.AttachBytes(second,new byte[]{0,255,13,10,123},"original.bin","application/octet-stream",second.EditVersion);Require(await session.SaveAsync(),"Nonempty attachment baseline saved");var baseline=session.Workspace.Capture();byte[] cipher=File.ReadAllBytes(Path.Combine(vaultRoot,"current.vault"));
                Recent(session.Workspace,profile,first);Recent(session.Workspace,profile,second);Recent(session.Workspace,profile,first);Recent(session.Workspace,other,first);Guid missingFolder=Guid.NewGuid();var saved=Save(session.Workspace,profile,"합성 저장 조건",new(){Query="SYNTHETIC",Field=SearchField.Body,FolderId=missingFolder,FavoriteOnly=true});
                var state=session.Workspace.Capture();Require(state.SchemaVersion==6&&state.UiDevices.Single(d=>d.UiDeviceId==profile).RecentNoteIds.SequenceEqual(new[]{first.Id,second.Id}),"Bounded unique MRU metadata and exact schema6");Require(state.Notes.SequenceEqual(baseline.Notes)&&state.History.SequenceEqual(baseline.History)&&state.AttachmentRootId==baseline.AttachmentRootId&&state.AttachmentObjects.SequenceEqual(baseline.AttachmentObjects),"Clean metadata changes preserve content revisions/history/exact original objects/root");Require(await session.SaveAsync(),"Actual schema6 metadata encrypted save");VerifyAttachment(session);Require(Directory.GetFiles(vaultRoot,"previous-*.vault").Any(path=>File.ReadAllBytes(path).SequenceEqual(cipher)),"First metadata migration preserves exact previous ciphertext");
                Require(session.Workspace.Folders.All(f=>f.FolderId!=missingFolder)&&session.Workspace.GetUiDevice(profile).SavedSearches.Single()==saved,"Missing folder remains an exact inert saved reference without broadening");session.Workspace.DeleteNote(first);Require(session.Workspace.GetUiDevice(profile).RecentNoteIds.SequenceEqual(new[]{second.Id})&&session.Workspace.GetUiDevice(other).RecentNoteIds.Length==0,"Trash sanitizes recent IDs across every profile atomically");
                remove.Invoke(session.Workspace,[profile,saved.Id]);clear.Invoke(session.Workspace,[profile]);Require(session.Workspace.Capture().SchemaVersion==6,"Emptying both collections preserves schema6");Save(session.Workspace,profile,saved.Name,saved.Options);Require(await session.SaveAsync()&&await session.BackupAsync(backup),"Encrypted latest metadata backup");var latest=session.Workspace.Capture();var old=latest with{SchemaVersion=5,UiDevices=latest.UiDevices.Select(d=>d with{RecentNoteIds=[],SavedSearches=[]}).ToArray()};VaultChecks.ExpectFailure(()=>session.Workspace.AcceptPrepared(old),"Workspace cannot downgrade after enabling metadata6");await session.LockAsync();
            }
            using(var reopened=new SaveCoordinator(EncryptedVault.Open(vaultRoot,secret),TimeProvider.System))
            {
                Require(reopened.Workspace.Capture().SchemaVersion==6&&reopened.Workspace.GetUiDevice(profile).SavedSearches.Single().Options.Query=="SYNTHETIC","Recovery-only restart retains exact saved conditions");VerifyAttachment(reopened);await reopened.LockAsync();
            }
            string restoredRoot=Path.Combine(root,"restored");string candidate=EncryptedVault.ImportEncryptedCopy(restoredRoot,backup,secret);using(var restored=new SaveCoordinator(EncryptedVault.Open(restoredRoot,secret,candidate),TimeProvider.System)){Require(restored.Workspace.GetUiDevice(profile).SavedSearches.Single().Options.FolderId is not null&&restored.Workspace.GetUiDevice(other).RecentNoteIds.Length==0,"Independent-root backup recovery retains all profiles and inert missing-folder condition");VaultChecks.ExpectFailure(()=>VerifyAttachment(restored),"Imported candidate cannot decrypt attachment before actual current anchoring");Require(await restored.SaveAsync(),"Recovered schema6 can save with owned root");VerifyAttachment(restored);await restored.LockAsync();}
            await ReservationAndHidden(Path.Combine(root,"counters"),secret,profile);
            await RootLock(Path.Combine(root,"root-lock"),secret,profile);
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
        Console.WriteLine("PASS: schema6 recent/saved encrypted migration, exact previous bytes/no content noise, all-profile trash sanitization, empty-state stickiness, restart and independent-root backup");
    }
    private static void FailureInvariance()
    {
        Guid profile=Guid.NewGuid();var fresh=new EditingWorkspace(TimeProvider.System);var note=fresh.CreateNote();VaultChecks.ExpectFailure(()=>fresh.RecordRecentNote(profile,note),"Unprepared root refuses metadata publication");var basis=fresh.Capture() with{SchemaVersion=5,AttachmentRootId=Guid.NewGuid()};fresh.Clear();var workspace=new EditingWorkspace(TimeProvider.System,basis);
        try
        {
            note=workspace.Notes.Single();int events=0;workspace.Changed+=()=>events++;string Snapshot()=>JsonSerializer.Serialize(workspace.Capture());string before=Snapshot();VaultChecks.ExpectFailure(()=>workspace.SaveSearch(profile,"invalid",new(){Query=new string('x',257)}),"Invalid saved query rejected before schema activation");Require(Snapshot()==before&&workspace.Capture().SchemaVersion==5&&events==0,"Failed metadata activation preserves complete state/schema/events");
            for(int i=0;i<20;i++)workspace.SaveSearch(profile,$"saved{i}",new(){Query=$"query{i}"});before=Snapshot();int count=events;VaultChecks.ExpectFailure(()=>workspace.SaveSearch(profile,"twentyfirst",new()),"Saved search capacity preflight");Require(Snapshot()==before&&events==count,"Capacity rejection preserves exact full metadata/content/schema/events");var existing=workspace.GetUiDevice(profile).SavedSearches[0];var updated=workspace.SaveSearch(profile,existing.Name,new(){Query="replaced"});Require(updated.Id==existing.Id&&workspace.GetUiDevice(profile).SavedSearches.Length==20,"Named overwrite preserves saved identity at capacity");
            workspace.RecordRecentNote(profile,note);count=events;workspace.RecordRecentNote(profile,note);Require(events==count,"MRU no-op creates no generation noise");before=Snapshot();var hidden=new EditingWorkspace(TimeProvider.System,basis,workspace.Capture());try{Require(hidden.Capture().SchemaVersion==6&&hidden.GetUiDevice(profile).SavedSearches.Length==20,"Displayed hidden schema6 survives older accepted basis");}finally{hidden.Clear();}
            var visits=Enumerable.Range(0,21).Select(_=>workspace.CreateNote()).ToArray();foreach(var visit in visits)workspace.RecordRecentNote(profile,visit);Require(workspace.GetUiDevice(profile).RecentNoteIds.SequenceEqual(visits.Skip(1).Reverse().Select(n=>n.Id)),"21 distinct visits retain exactly newest20 in MRU order");workspace.RecordRecentNote(profile,visits[7]);var mru=workspace.GetUiDevice(profile).RecentNoteIds;Require(mru.Length==20&&mru[0]==visits[7].Id&&mru.Distinct().Count()==20,"Revisited retained note promoted without duplication");workspace.ClearRecentNotes(profile);Require(workspace.GetUiDevice(profile).RecentNoteIds.Length==0&&workspace.Capture().SchemaVersion==6,"Explicit clear preserves metadata6 even with empty recents");
        }
        finally{workspace.Clear();}
    }
    private static void Require(bool condition,string message)=>VaultChecks.Require(condition,message);
    private static void PayloadFailure()
    {
        Guid id=Guid.NewGuid(),profile=Guid.NewGuid();var now=DateTimeOffset.UnixEpoch;var note=new StoredNote(id,Guid.NewGuid(),[],now,now,"near","body");var history=Enumerable.Range(0,255).Select(_=>new StoredRevision(id,Guid.NewGuid(),[],now,"h",new string('x',65536))).ToArray();var candidate=new VaultSnapshot(5,Guid.NewGuid(),[note]){AttachmentRootId=Guid.NewGuid(),History=history};int limit=VaultEnvelope.MaxFile-308;int remaining=history[^1].Text.Length-(SnapshotSerialization.Bytes(candidate).Length-(limit-8));Require(remaining is >=0 and <=65536,"Schema5 metadata near-payload fixture");history[^1]=history[^1] with{Text=new string('x',remaining)};VaultEnvelope.Validate(candidate);var workspace=new EditingWorkspace(TimeProvider.System,candidate);try{byte[] before=SnapshotSerialization.Bytes(workspace.Capture());int events=0;workspace.Changed+=()=>events++;VaultChecks.ExpectFailure(()=>workspace.RecordRecentNote(profile,workspace.Notes.Single()),"Full payload cannot accept metadata activation");Require(events==0&&workspace.Capture().SchemaVersion==5&&SnapshotSerialization.Bytes(workspace.Capture()).SequenceEqual(before),"Payload refusal preserves exact profile/content/schema/accepted basis/events");}finally{workspace.Clear();}
    }
    private static void VerifyAttachment(SaveCoordinator session)
    {
        var note=session.Workspace.Notes.Single(n=>!n.IsDeleted&&n.AttachmentIds.Length==1);var bytes=session.ReadAttachmentBytes(note,note.AttachmentIds.Single(),note.EditVersion);try{Require(bytes.SequenceEqual(new byte[]{0,255,13,10,123}),"Actual original object authentication survives schema6 migration/restart/backup");}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    private static async Task ReservationAndHidden(string root,byte[] secret,Guid profile)
    {
        var wraps=typeof(EncryptedVault).GetField("wraps",BindingFlags.Instance|BindingFlags.NonPublic)!;var key=typeof(EncryptedVault).GetField("vaultKey",BindingFlags.Instance|BindingFlags.NonPublic)!;
        using(var vault=EncryptedVault.Create(root,secret,secret))
        {
            var five=vault.InitializeAttachmentRoot(vault.Loaded);vault.Save(five);var six=five with{SchemaVersion=6,UiDevices=[new(profile,new(),[]){SavedSearches=[new(Guid.NewGuid(),"saved",new(){Query="counter"})]}]};ulong before=(ulong)wraps.GetValue(vault)!;var actual=(byte[])key.GetValue(vault)!;key.SetValue(vault,new byte[31]);try{VaultChecks.ExpectFailure(()=>vault.Prepare(six),"Failed schema6 encryption preparation");}finally{key.SetValue(vault,actual);}Require((ulong)wraps.GetValue(vault)! ==before+3,"Failed schema6 prepare reserves allthree wraps");var prepared=vault.Prepare(six);Require((ulong)wraps.GetValue(vault)! ==before+6,"Retry reserves anotherthree wraps");wraps.SetValue(vault,VaultEnvelope.MaxWraps-2);VaultChecks.ExpectFailure(()=>vault.Prepare(six),"Schema6 needs three remaining wraps");Require((ulong)wraps.GetValue(vault)! ==VaultEnvelope.MaxWraps-2,"Near-limit refusal preserves reservation counter");wraps.SetValue(vault,before+6);VaultChecks.ExpectFailure(()=>vault.Prepare(five),"Prepared metadata6 refuses root-preserving schema5 downgrade");VaultChecks.ExpectFailure(()=>vault.InitializeAttachmentRoot(five),"Root initialization cannot discard prepared metadata6");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(five),"Hidden recovery cannot discard prepared metadata6");VaultChecks.ExpectFailure(()=>vault.ValidateHiddenRoot(six with{SchemaVersion=9}),"Unknown hidden future schema refused");vault.Commit(prepared);
        }
        using(var vault=EncryptedVault.Open(root,secret))using(var session=new SaveCoordinator(vault,TimeProvider.System))
        {
            session.Workspace.SaveSearch(profile,"saved",new(){Query="latest hidden metadata"});ulong before=(ulong)wraps.GetValue(vault)!;wraps.SetValue(vault,VaultEnvelope.MaxWraps);Require(!await session.LockAsync()&&session.IsLocked&&!session.KeysReleased,"Pre-encryption failure conceals metadata and retains explicit hidden-recovery keys");VaultChecks.ExpectFailure(()=>session.ResumeHidden(new byte[32]),"Wrong secret cannot resume hidden metadata");session.ResumeHidden(secret);Require(session.Workspace.Capture().SchemaVersion==6&&session.Workspace.GetUiDevice(profile).SavedSearches.Single().Options.Query=="latest hidden metadata","Hidden recovery restores exact latest metadata6 despite older accepted basis");wraps.SetValue(vault,before);Require(await session.SaveAsync(),"Resumed metadata encrypted after restoring synthetic budget");Require(await session.LockAsync()&&session.KeysReleased,"Successful hidden recovery settles and releases keys");
        }
    }
    private static async Task RootLock(string root,byte[] secret,Guid profile)
    {
        var files=new PausedRootFiles();using(var session=new SaveCoordinator(EncryptedVault.Create(root,secret,secret,files),TimeProvider.System))
        {
            var note=session.Workspace.CreateNote();note.Text="initial root";var preparation=session.PrepareAttachmentsAsync();await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=session.LockAsync();Require(session.IsLocked&&session.KeysReleased&&note.Text=="","Lock during initial root prepare conceals/zeroes source and releases keys before blocked ciphertext I/O");VaultChecks.ExpectFailure(()=>session.Workspace.RecordRecentNote(profile,note),"Late root completion cannot publish metadata into revoked workspace");files.Continue.TrySetResult();Require(!await preparation,"Late initial root anchoring reports no active metadata authority");await locking;
        }
        using var reopened=EncryptedVault.Open(root,secret);Require(reopened.Loaded.SchemaVersion==5&&reopened.Loaded.UiDevices.All(d=>d.RecentNoteIds.Length==0&&d.SavedSearches.Length==0),"Root-only completion never published metadata6");
    }
    private sealed class PausedRootFiles:IAtomicVaultFiles
    {
        private readonly AtomicVaultFiles actual=new();internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Continue=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream CreateNew(string path){var output=actual.CreateNew(path);Entered.TrySetResult();if(!Continue.Task.Wait(TimeSpan.FromSeconds(10))){output.Dispose();throw new IOException("Synthetic initial-root pause timed out");}return output;}
        public void FlushToDisk(Stream stream)=>actual.FlushToDisk(stream);public void Move(string temporary,string current)=>actual.Move(temporary,current);public void Replace(string temporary,string current,string previous)=>actual.Replace(temporary,current,previous);
    }
}

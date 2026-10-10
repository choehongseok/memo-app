using System.Collections.Immutable;
using System.Collections.ObjectModel;
using MemoApp.Core.Storage;
using MemoApp.Core.Documents;
namespace MemoApp.Core.Editing;

public sealed partial class EditingWorkspace
{
    private readonly TimeProvider clock;
    private readonly NoteCollection notes = [];
    private readonly List<StoredFolder> folders = [];
    private readonly List<StoredTag> tags = [];
    private readonly List<StoredDeviceUi> devices = [];
    private Dictionary<Guid, long> acceptedVersions = [];
    private VaultSnapshot basis;
    private Guid attachmentRootId;
    private ImmutableArray<StoredAttachmentObject> attachmentObjects=[];
    private bool closed;
    private long ocrAuthorityEpoch;
    private bool searchStateEnabled,filePathsEnabled,discardedEnabled,backupPolicyEnabled,inlineImagesEnabled,ocrEnabled;
    private StoredDiscardedRevision[] discardedRevisions=[];
    private StoredTombstone[] discardedMarkers=[];
    public EditingWorkspace(TimeProvider clock, VaultSnapshot? initial = null, VaultSnapshot? displayed = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); Notes = new(notes);
        // Keep the last accepted revision basis separate from unsaved (possibly invalid) drafts.
        // Recovery supplies its entire latest organization state, not a stale vault.Loaded projection.
        basis = initial ?? new(4, Guid.NewGuid(), []);
        var visible = displayed ?? basis;
        searchStateEnabled=visible.SchemaVersion>=6||basis.SchemaVersion>=6;
        filePathsEnabled=visible.SchemaVersion>=7||basis.SchemaVersion>=7;
        ocrEnabled=visible.SchemaVersion==11||basis.SchemaVersion==11;
        inlineImagesEnabled=visible.SchemaVersion>=10||basis.SchemaVersion>=10;
        backupPolicyEnabled=visible.SchemaVersion>=9||basis.SchemaVersion>=9;
        discardedEnabled=visible.SchemaVersion>=8||basis.SchemaVersion>=8;
        if(discardedEnabled)discardedRevisions=DiscardedEvidence.Clone(visible.DiscardedRevisions);
        discardedMarkers=DiscardedEvidence.ContentlessMarkers(visible);
        attachmentRootId=visible.AttachmentRootId;attachmentObjects=visible.AttachmentObjects;
        folders.AddRange(visible.Folders); tags.AddRange(visible.Tags); devices.AddRange(visible.UiDevices);
        foreach (var source in visible.Notes)
        {
            AddDraft(new(clock, source));
            var old = basis.Notes.FirstOrDefault(n => n.NoteId == source.NoteId);
            acceptedVersions[source.NoteId] = old is not null && old.Title == source.Title && old.Text == source.Text && old.Metadata == source.Metadata && old.Mode==source.Mode && old.Document==source.Document&&old.AttachmentIds.SequenceEqual(source.AttachmentIds) ? 0 : -1;
        }
    }
    public ReadOnlyObservableCollection<NoteDraft> Notes { get; }
    public IReadOnlyList<StoredFolder> Folders => folders.AsReadOnly();
    public IReadOnlyList<StoredTag> Tags => tags.AsReadOnly();
    public event Action? Changed;
    internal event Action<NoteDraft?>? AttachmentReadInvalidating;
    private void InvalidateAttachmentReads(NoteDraft? source)
    {
        ocrAuthorityEpoch++;
        if (AttachmentReadInvalidating is not { } handlers) return;
        foreach (Action<NoteDraft?> handler in handlers.GetInvocationList()) { try { handler(source); } catch { } }
    }
    public void SetRichDocument(NoteDraft note,StyledDocument document)
    {
        RequireNote(note);ArgumentNullException.ThrowIfNull(document);if(note.Document?.SchemaVersion==2||document.SchemaVersion==2)throw new InvalidOperationException("Image documents require explicit canonical operations");if(note.Mode!="rich"||note.Document is null||!RichDocumentCodec.Inspect(note.Document).Supported)throw new InvalidOperationException("Rich original is not editable");
        var info=RichDocumentCodec.Inspect(document);if(!info.Supported)throw new InvalidOperationException("Unsupported new rich document");
        if(note.Document==document)return;
        var before=Capture();var current=before.Notes.Single(n=>n.NoteId==note.Id);var history=before.History.ToList();var now=clock.GetUtcNow();
        bool accepted=acceptedVersions.TryGetValue(note.Id,out var version)&&version==note.EditVersion;
        if(accepted)
        {
            history.Add(new(current.NoteId,current.RevisionId,(Guid[])current.Parents.Clone(),current.ModifiedAt,current.Title,current.Text){Metadata=current.Metadata,Mode=current.Mode,Document=current.Document,AttachmentIds=current.AttachmentIds});
            current=current with{RevisionId=Guid.NewGuid(),Parents=[current.RevisionId]};
        }
        current=current with{Text=info.Text!,Document=document,ModifiedAt=now};
        var candidate=before with{Notes=before.Notes.Select(n=>n.NoteId==note.Id?current:n).ToArray(),History=history.ToArray()};VaultEnvelope.Validate(candidate);
        // Like plain typing, content changes remain a dirty draft until preparation. Do not freeze one history revision per keystroke.
        note.StageEvent(current);Changed?.Invoke();if(!closed)note.PublishEvent();
    }
    public void ConvertMode(NoteDraft note,string mode,bool confirmedLoss=false)
    {
        RequireNote(note);if(mode is not ("plain" or "rich" or "markdown"))throw new ArgumentException("Unsupported document mode");if(note.Mode==mode)return;
        if(!confirmedLoss)throw new InvalidOperationException("Explicit conversion loss acknowledgement required");
        var document=mode=="rich"?RichDocumentCodec.FromPlain(note.Text):null;string text=document is null?note.Text:RichDocumentCodec.Inspect(document).Text!;
        ApplyContentEvent(note,text,mode,document,note.Metadata);
    }
    public StoredDeviceUi GetUiDevice(Guid profile)
    {
        EnsureOpen(); if(profile==Guid.Empty)throw new ArgumentException("Empty UI profile");
        return devices.SingleOrDefault(d=>d.UiDeviceId==profile) ?? new(profile,new(),[]);
    }
    private void SetUiDevice(StoredDeviceUi next,bool activateSearch=false)
    {
        EnsureOpen(); var updated=devices.Where(d=>d.UiDeviceId!=next.UiDeviceId).Append(next).ToArray();
        var candidate=Capture() with {UiDevices=updated};if(activateSearch&&candidate.SchemaVersion<6)candidate=candidate with{SchemaVersion=6};VaultEnvelope.Validate(candidate);
        if(devices.SingleOrDefault(d=>d.UiDeviceId==next.UiDeviceId)==next)return;
        if(activateSearch)searchStateEnabled=true;devices.Clear();devices.AddRange(updated);Changed?.Invoke();
    }
    public void SetAutomaticBackupPolicy(Guid profile,StoredAutomaticBackupPolicy? policy)
    {
        var device=GetUiDevice(profile);if(device.AutomaticBackupPolicy==policy)return;
        var updated=devices.Where(d=>d.UiDeviceId!=profile).Append(device with{AutomaticBackupPolicy=policy}).ToArray();
        var before=Capture();var candidate=before with{SchemaVersion=Math.Max(9,before.SchemaVersion),UiDevices=updated};VaultEnvelope.Validate(candidate);
        backupPolicyEnabled=true;discardedEnabled=true;devices.Clear();devices.AddRange(updated);Changed?.Invoke();
    }
    public void SetUiPreferences(Guid profile,UiPreferences preferences)
    {
        var device=GetUiDevice(profile); if(device.Preferences==preferences)return;
        SetUiDevice(device with {Preferences=preferences});
    }
    public void SetWindowLayout(Guid profile,StoredWindowLayout layout)
    {
        var device=GetUiDevice(profile);
        if(device.Windows.Any(w=>w.Kind==layout.Kind&&w.NoteId==layout.NoteId&&w==layout))return;
        SetUiDevice(device with {Windows=device.Windows.Where(w=>w.Kind!=layout.Kind||w.NoteId!=layout.NoteId).Append(layout).ToImmutableArray()});
    }
    internal VaultSnapshot FrozenBasis => basis;
    private void EnsureOpen() { if (closed) throw new InvalidOperationException("Editing workspace is closed"); }
    private void RequireNote(NoteDraft note, bool allowTrash = false)
    {
        EnsureOpen();
        if (!notes.Contains(note) || note.IsClosed || note.IsDeleted && !allowTrash) throw new InvalidOperationException("Note is not editable in this workspace");
    }
    private void AddDraft(NoteDraft note)
    {
        note.OcrEditPreflight = PreflightOcrEdit;
        note.AttachmentReadInvalidating += () => InvalidateAttachmentReads(note);
        note.PropertyChanged += (_, e) => { if (!closed && e.PropertyName == nameof(NoteDraft.EditVersion)) Changed?.Invoke(); };
        notes.Register(note);
    }
    public NoteDraft CreateNote()
    {
        EnsureOpen();
        if (notes.Count >= 100) throw new InvalidOperationException("Trial note limit reached (including trash)");
        var note = new NoteDraft(clock, NextOrder()); AddDraft(note);
        Changed?.Invoke(); if (!closed) notes.PublishAdded(note); return note;
    }
    private static string Name(string value)
    {
        ArgumentNullException.ThrowIfNull(value); value = value.Trim();
        if (value.Length is < 1 or > 128 || value.Any(char.IsControl) || !RichDocumentCodec.IsWellFormedUnicode(value)) throw new ArgumentException("Name outside limits");
        return value;
    }
    public StoredFolder CreateFolder(string name, Guid? parentId = null)
    {
        EnsureOpen(); name = Name(name);
        if (folders.Count >= 100 || parentId is not null && folders.All(f => f.FolderId != parentId) || folders.Any(f => f.ParentId == parentId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Folder parent/name/limit rejected");
        var folder = new StoredFolder(Guid.NewGuid(), parentId, name);
        VaultEnvelope.Validate(Capture() with{Folders=folders.Append(folder).ToArray()});
        folders.Add(folder); Changed?.Invoke(); return folder;
    }
    public void MoveNote(NoteDraft note, Guid? folderId)
    {
        RequireNote(note);
        if (folderId is not null && folders.All(f => f.FolderId != folderId)) throw new ArgumentException("Unknown folder");
        note.SetMetadata(note.Metadata with { FolderId = folderId });
    }
    public void SetTags(NoteDraft note, IEnumerable<string> names)
    {
        RequireNote(note); ArgumentNullException.ThrowIfNull(names);
        var requested = names.Select(Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length > 16 || tags.Count + requested.Count(n => tags.All(t => !string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))) > 100) throw new InvalidOperationException("Tag budget exceeded");
        var staged=tags.ToList();
        var ids = requested.Select(name =>
        {
            var existing = staged.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) { existing = new(Guid.NewGuid(), name); staged.Add(existing); }
            return existing.TagId;
        }).Order().ToImmutableArray();
        if (note.Metadata.TagIds.SequenceEqual(ids)) return;
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,note.Text,note.Metadata with{TagIds=ids})},stagedTags:staged.ToArray());
    }
    public void SetImportant(NoteDraft note, bool value) { RequireNote(note); note.Important = value; }
    private int NextOrder() => notes.Count == 0 ? 0 : Math.Min(1000000, notes.Max(n => n.Metadata.Order) + 1);
    private void ApplyEvent(NoteDraft note, string title, string text, NoteMetadata metadata) =>
        ApplyEvents(new Dictionary<Guid, (string Title, string Text, NoteMetadata Metadata)> { [note.Id] = (title, text, metadata) });
    private void ApplyContentEvent(NoteDraft note,string text,string mode,StyledDocument? document,NoteMetadata metadata)=>
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,text,metadata)},new Dictionary<Guid,(string Mode,StyledDocument? Document)>{[note.Id]=(mode,document)});
    private void ApplyEvents(IReadOnlyDictionary<Guid, (string Title, string Text, NoteMetadata Metadata)> changes,IReadOnlyDictionary<Guid,(string Mode,StyledDocument? Document)>? formats=null,StoredTag[]? stagedTags=null,IReadOnlyDictionary<Guid,ImmutableArray<Guid>>? attachments=null,ImmutableArray<StoredAttachmentObject>? stagedObjects=null,bool activateFilePaths=false,bool activateInlineImages=false)
    {
        if (changes.Count == 0) return;
        var before = Capture(); VaultEnvelope.Validate(before); var history = before.History.ToList();
        var nextNotes = before.Notes.Select(previous =>
        {
            if (!changes.TryGetValue(previous.NoteId, out var change)) return previous;
            history.Add(new(previous.NoteId, previous.RevisionId, (Guid[])previous.Parents.Clone(), previous.ModifiedAt, previous.Title, previous.Text) { Metadata = previous.Metadata,Mode=previous.Mode,Document=previous.Document,AttachmentIds=previous.AttachmentIds });
            var format=formats is not null&&formats.TryGetValue(previous.NoteId,out var updated)?updated:(previous.Mode,previous.Document);
            var refs=attachments is not null&&attachments.TryGetValue(previous.NoteId,out var nextRefs)?nextRefs:previous.AttachmentIds;
            return previous with { RevisionId = Guid.NewGuid(), Parents = [previous.RevisionId], ModifiedAt = clock.GetUtcNow(), Title = change.Title, Text = change.Text, Metadata = change.Metadata,Mode=format.Item1,Document=format.Item2,AttachmentIds=refs };
        }).ToArray();
        var tombstones = before.Tombstones.Where(t => nextNotes.All(n => n.NoteId != t.NoteId))
            .Concat(nextNotes.Where(n => n.Metadata.Deleted).Select(n => new StoredTombstone(n.NoteId, n.RevisionId, (Guid[])n.Parents.Clone()))).ToArray();
        var next = before with { Notes = nextNotes, History = history.ToArray(), Tombstones = tombstones,Tags=stagedTags??before.Tags,AttachmentObjects=stagedObjects??before.AttachmentObjects };
        if(activateInlineImages&&next.SchemaVersion<10)next=next with{SchemaVersion=10};
        if(activateFilePaths&&next.SchemaVersion<7)next=next with{SchemaVersion=7};
        if(searchStateEnabled)next=next with{UiDevices=SanitizeRecent(nextNotes)};
        VaultEnvelope.Validate(next);
        var affected = notes.Where(n => changes.ContainsKey(n.Id)).ToArray();
        foreach (var note in affected) note.StageEvent(nextNotes.Single(n => n.NoteId == note.Id),formats?.ContainsKey(note.Id)==true||attachments?.ContainsKey(note.Id)==true);
        if(stagedTags is not null){tags.Clear();tags.AddRange(stagedTags);}
        if(searchStateEnabled){devices.Clear();devices.AddRange(next.UiDevices);}
        AcceptPrepared(next); Changed?.Invoke();
        foreach (var note in affected) { if (closed) break; note.PublishEvent(); }
    }
    public NoteDraft ImportMarkdown(string title,string raw,Guid? folderId=null)=>
        AddComplete(title,raw,new(){FolderId=folderId,Order=NextOrder()},"markdown");
    public NoteDraft ImportText(string title, string text, Guid? folderId = null) =>
        AddComplete(title,text,new() { FolderId=folderId,Order=NextOrder() });
    private NoteDraft AddComplete(string title,string text,NoteMetadata metadata,string mode="plain",StyledDocument? document=null,ImmutableArray<Guid> attachments=default)
    {
        EnsureOpen(); var before = Capture(); var now = clock.GetUtcNow();
        var source = new StoredNote(Guid.NewGuid(), Guid.NewGuid(), [], now, now, title, text,mode) { Metadata=metadata,Document=document,AttachmentIds=attachments.IsDefault?[]:attachments };
        var next = before with { Notes=before.Notes.Append(source).ToArray() }; VaultEnvelope.Validate(next);
        var draft = new NoteDraft(clock,source); AddDraft(draft); AcceptPrepared(next);
        Changed?.Invoke(); if (!closed) notes.PublishAdded(draft); return draft;
    }
    public void DeleteNote(NoteDraft note)
    {
        RequireNote(note); ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = true });
    }
    private NoteDraft[] BatchSelection(IEnumerable<NoteDraft> selected,bool deleted)
    {
        EnsureOpen();ArgumentNullException.ThrowIfNull(selected);var batch=selected.Take(101).ToArray();
        if(batch.Length is <1 or >100 || batch.Any(n=>n is null) || batch.Select(n=>n.Id).Distinct().Count()!=batch.Length)throw new ArgumentException("Invalid batch selection/count");
        foreach(var note in batch){RequireNote(note,true);if(note.IsDeleted!=deleted)throw new InvalidOperationException("Batch contains incompatible note states");}
        return batch;
    }
    public void MoveNotes(IEnumerable<NoteDraft> selected,Guid? folderId)
    {
        var batch=BatchSelection(selected,false);
        if(folderId is Guid id && folders.All(f=>f.FolderId!=id))throw new ArgumentException("Unknown batch target folder");
        ApplyEvents(batch.Where(n=>n.FolderId!=folderId).ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{FolderId=folderId})));
    }
    public void DeleteNotes(IEnumerable<NoteDraft> selected)
    {
        var batch=BatchSelection(selected,false);ApplyEvents(batch.ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{Deleted=true})));
    }
    public void RestoreNotes(IEnumerable<NoteDraft> selected)
    {
        var batch=BatchSelection(selected,true);ApplyEvents(batch.ToDictionary(n=>n.Id,n=>(n.Title,n.Text,n.Metadata with{Deleted=false})));
    }
    public void RestoreNote(NoteDraft note)
    {
        RequireNote(note, true);
        if (!note.IsDeleted) return;
        ApplyEvent(note, note.Title, note.Text, note.Metadata with { Deleted = false });
    }
    public void ReorderBefore(NoteDraft note, NoteDraft target)
    {
        RequireNote(note); RequireNote(target);
        if (note == target) return;
        if (note.Pinned != target.Pinned) throw new InvalidOperationException("List-pinned and ordinary groups are separate");
        var ordered = notes.Where(n => !n.IsDeleted).OrderBy(n => n.Metadata.Order).ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToList();
        var original = ordered.ToArray(); ordered.Remove(note); ordered.Insert(ordered.IndexOf(target), note);
        if (ordered.SequenceEqual(original)) return;
        var changes = new Dictionary<Guid, (string Title, string Text, NoteMetadata Metadata)>();
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i].Metadata.Order != i) changes.Add(ordered[i].Id, (ordered[i].Title, ordered[i].Text, ordered[i].Metadata with { Order = i }));
        ApplyEvents(changes);
    }
    public NoteDraft Duplicate(NoteDraft note)
    {
        RequireNote(note); return AddComplete(note.Title,note.Text,note.Metadata with { Deleted=false,Order=NextOrder() },note.Mode,note.Document,note.AttachmentIds);
    }
    public IReadOnlyList<StoredRevision> HistoryFor(NoteDraft note)
    {
        RequireNote(note, true);
        return basis.History.Where(h => h.NoteId == note.Id).Reverse().ToArray();
    }
    public void RestoreRevision(NoteDraft note, Guid revisionId)
    {
        RequireNote(note);
        var revision = basis.History.SingleOrDefault(h => h.NoteId == note.Id && h.RevisionId == revisionId) ?? throw new ArgumentException("Unknown revision");
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(revision.Title,revision.Text,revision.Metadata with{Deleted=false})},new Dictionary<Guid,(string Mode,StyledDocument? Document)>{[note.Id]=(revision.Mode,revision.Document)},attachments:new Dictionary<Guid,ImmutableArray<Guid>>{[note.Id]=revision.AttachmentIds});
    }
    public Guid[] DescendantFolders(Guid parentId)
    {
        EnsureOpen(); if (folders.All(f => f.FolderId != parentId)) throw new ArgumentException("Unknown folder");
        var ids = new HashSet<Guid> { parentId };
        bool added; do { added = false; foreach (var f in folders) if (f.ParentId is Guid p && ids.Contains(p)) added |= ids.Add(f.FolderId); } while (added);
        return ids.ToArray();
    }
    internal void RequireAttachmentNote(NoteDraft note)=>RequireNote(note);
    public AttachmentDescription[] DescribeAttachments(NoteDraft note)
    {
        RequireNote(note,true);
        return note.AttachmentIds.Select(id=>attachmentObjects.Single(item=>item.ObjectId==id))
            .Select(item=>new AttachmentDescription(item.ObjectId,item.Name,item.Mime,item.Length,item.Sha256)).ToArray();
    }
    internal void AddAttachment(NoteDraft note,StoredAttachmentObject item)
    {
        RequireNote(note);ArgumentNullException.ThrowIfNull(item);
        if(attachmentRootId==Guid.Empty||attachmentObjects.Any(x=>x.ObjectId==item.ObjectId))throw new InvalidOperationException("Attachment root/object identity unavailable or reused");
        AttachmentValidation.Object(item,attachmentRootId);
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,note.Text,note.Metadata)},attachments:new Dictionary<Guid,ImmutableArray<Guid>>{[note.Id]=note.AttachmentIds.Add(item.ObjectId)},stagedObjects:attachmentObjects.Add(item));
    }
    internal void DetachAttachment(NoteDraft note,Guid id)
    {
        RequireNote(note);
        if(!note.AttachmentIds.Contains(id))throw new ArgumentException("Unknown active attachment reference");
        if(note.Document is not null&&RichDocumentCodec.Images(note.Document).Any(image=>image.AttachmentId==id))throw new InvalidOperationException("Remove active image blocks before detaching their attachment");
        ApplyEvents(new Dictionary<Guid,(string Title,string Text,NoteMetadata Metadata)>{[note.Id]=(note.Title,note.Text,note.Metadata with{AttachmentOcrResults=note.Metadata.AttachmentOcrResults.Where(r=>r.ObjectId!=id).ToImmutableArray()})},attachments:new Dictionary<Guid,ImmutableArray<Guid>>{[note.Id]=note.AttachmentIds.Remove(id)});
    }
    internal StoredAttachmentObject AttachmentObject(NoteDraft note,Guid id)
    {
        RequireNote(note);
        if(!note.AttachmentIds.Contains(id))throw new InvalidOperationException("Attachment reference is not in this active note");
        return attachmentObjects.Single(item=>item.ObjectId==id);
    }
    public VaultSnapshot Capture()
    {
        EnsureOpen(); var history = basis.History.ToList();
        var current = notes.Select(draft =>
        {
            var old = basis.Notes.FirstOrDefault(n => n.NoteId == draft.Id);
            if (old is not null && acceptedVersions.TryGetValue(draft.Id, out var accepted) && draft.EditVersion == accepted) return old;
            if (old is not null) history.Add(new(old.NoteId, old.RevisionId, (Guid[])old.Parents.Clone(), old.ModifiedAt, old.Title, old.Text) { Metadata = old.Metadata,Mode=old.Mode,Document=old.Document,AttachmentIds=old.AttachmentIds });
            return new StoredNote(draft.Id, Guid.NewGuid(), old is null ? [] : [old.RevisionId], draft.CreatedAt, draft.ModifiedAt, draft.Title, draft.Text,draft.Mode) { Metadata = draft.Metadata,Document=draft.Document,AttachmentIds=draft.AttachmentIds };
        }).ToArray();
        var contentless = basis.Tombstones.Where(t => current.All(n => n.NoteId != t.NoteId));
        var tombstones = contentless.Concat(current.Where(n => n.Metadata.Deleted).Select(n => new StoredTombstone(n.NoteId, n.RevisionId, (Guid[])n.Parents.Clone()))).ToArray();
        return new(ocrEnabled?11:inlineImagesEnabled?10:backupPolicyEnabled?9:discardedEnabled?8:filePathsEnabled?7:searchStateEnabled?6:attachmentRootId==Guid.Empty?4:5, basis.DeviceId, current) { History = history.ToArray(), Tombstones = tombstones, DiscardedRevisions=DiscardedEvidence.Clone(discardedRevisions), Folders = folders.ToArray(), Tags = tags.ToArray(),UiDevices=devices.ToArray(),AttachmentRootId=attachmentRootId,AttachmentObjects=attachmentObjects };
    }
    public void AcceptPrepared(VaultSnapshot snapshot)
    {
        EnsureOpen();
        if(snapshot.SchemaVersion>=8)VaultEnvelope.Validate(snapshot);
        if(ocrEnabled&&snapshot.SchemaVersion!=11)throw new InvalidOperationException("OCR payload downgrade refused");
        if(inlineImagesEnabled&&snapshot.SchemaVersion<10)throw new InvalidOperationException("Inline image payload downgrade refused");
        if(backupPolicyEnabled&&snapshot.SchemaVersion<9)throw new InvalidOperationException("Backup policy downgrade refused");
        if(discardedEnabled&&snapshot.SchemaVersion<8)throw new InvalidOperationException("Discarded evidence downgrade refused");
        if(discardedEnabled||snapshot.SchemaVersion>=8)DiscardedEvidence.RequirePreserved(discardedRevisions,discardedMarkers,snapshot);
        if(filePathsEnabled&&snapshot.SchemaVersion<7)throw new InvalidOperationException("File path metadata downgrade refused");
        if(searchStateEnabled&&snapshot.SchemaVersion<6)throw new InvalidOperationException("Search UI state downgrade refused");
        InvalidateAttachmentReads(null);
        if(snapshot.SchemaVersion==11)ocrEnabled=true;
        if(snapshot.SchemaVersion>=10)inlineImagesEnabled=true;
        if(snapshot.SchemaVersion>=9)backupPolicyEnabled=true;
        if(snapshot.SchemaVersion>=6)searchStateEnabled=true;
        if(snapshot.SchemaVersion>=7)filePathsEnabled=true;
        if(snapshot.SchemaVersion>=8){discardedEnabled=true;discardedRevisions=DiscardedEvidence.Clone(snapshot.DiscardedRevisions);}
        discardedMarkers=DiscardedEvidence.ContentlessMarkers(snapshot);
        basis = snapshot;attachmentRootId=snapshot.AttachmentRootId;attachmentObjects=snapshot.AttachmentObjects;
        foreach (var draft in notes) acceptedVersions[draft.Id] = draft.EditVersion;
    }
    public void Clear()
    {
        if (closed) return;
        InvalidateAttachmentReads(null);
        closed = true; foreach (var note in notes.ToArray()) note.Close();
        ocrEnabled=false;inlineImagesEnabled=false;notes.Clear(); folders.Clear(); tags.Clear(); devices.Clear();acceptedVersions.Clear();discardedRevisions=[];discardedMarkers=[];attachmentRootId=Guid.Empty;attachmentObjects=[]; basis = new(4, Guid.Empty, []);
    }
}

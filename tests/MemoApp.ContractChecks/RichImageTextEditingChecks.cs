using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

internal static class RichImageTextEditingChecks
{
    internal static Task Run()
    {
        Require(typeof(SaveCoordinator).GetMethod("CaptureRichImageTextEditContext", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Closed source-bound rich image text editing API missing");
        RefusedRegistryDrop(); RegistryDrop(); BasicAndDirtyHistory(); ImageRefusals(); Authority(); Reentry(); Budgets(); ManifestAuthority(); PersistenceAndRecovery();
        Console.WriteLine("PASS: H01 closed canonical v2 text transactions, exact image/raw/source authority, single dirty notification, registry cleanup, history/OCR budgets and encrypted lifecycle");
        return Task.CompletedTask;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "memo-h01-text-" + Guid.NewGuid().ToString("N"));
        internal readonly byte[] Secret = EncryptedVault.GenerateRecoverySecret();
        internal EncryptedVault Vault;
        internal SaveCoordinator Owner;
        internal NoteDraft Note;
        internal readonly int ThreadId;
        internal readonly Guid Image;
        internal Action? OwnerProbe;
        private bool CheckAccess()
        { OwnerProbe?.Invoke(); return Environment.CurrentManagedThreadId==ThreadId; }
        internal Fixture(bool save = true)
        {
            Vault = EncryptedVault.Create(Root, Secret, Secret); Owner = new(Vault, TimeProvider.System);
            Note = Owner.Workspace.CreateNote(); Note.Title = "합성 v2 편집"; Note.Text = "FIRST";
            Require(Owner.PrepareAttachmentsAsync().GetAwaiter().GetResult(), "Actual root anchor");
            Image = Owner.AttachBytes(Note, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/AAECAwQAGQALubDj6wAAAABJRU5ErkJggg=="), "synthetic.png", "image/png", Note.EditVersion);
            Owner.Workspace.ConvertMode(Note, "rich", true);
            Call(Owner.Workspace, "InsertInlineImage", Note, Image, 0, "first");
            Call(Owner.Workspace, "InsertInlineImage", Note, Image, 2, "middle");
            Call(Owner.Workspace, "InsertInlineImage", Note, Image, 3, "last");
            if (save) Require(Owner.SaveAsync().GetAwaiter().GetResult(), "Saved authenticated canonical v2");
            ThreadId = Environment.CurrentManagedThreadId;
            Call(Owner, "RegisterRichImageTextEditOwner", (Func<bool>)CheckAccess);
        }
        internal object Capture(NoteDraft? note = null) { note ??= Note; return Call(Owner, "CaptureRichImageTextEditContext", note, note.EditVersion, Owner.AttachmentPreviewEpoch)!; }
        internal object Apply(object context, StyledDocument candidate) => Call(Owner, "ApplyRichImageTextEdit", context, candidate)!;
        internal bool Current(object context) => (bool)Call(Owner, "IsRichImageTextEditContextCurrent", context)!;
        internal StyledDocument Edit(string text) => new(2, Note.Document!.SourceJson.Replace("FIRST", text, StringComparison.Ordinal));
        internal byte[] Cipher() => File.ReadAllBytes(Path.Combine(Root, "current.vault"));
        internal void Reload(VaultSnapshot snapshot)
        {
            var prepared=Vault.Prepare(snapshot); Vault.Commit(prepared);
            Reopen();
        }
        internal void Reopen()
        {
            Guid noteId=Note.Id;
            Owner.Dispose();
            Vault=EncryptedVault.Open(Root,Secret); Owner=new(Vault,TimeProvider.System);
            Note=Owner.Workspace.Notes.Single(n=>n.Id==noteId);
            Call(Owner,"RegisterRichImageTextEditOwner",(Func<bool>)CheckAccess);
        }
        public void Dispose() { Owner.Dispose(); CryptographicOperations.ZeroMemory(Secret); Directory.Delete(Root, true); }
    }
    private static void BasicAndDirtyHistory()
    {
        using var f = new Fixture(); var note = f.Note; var initial = f.Owner.Workspace.Capture(); var source = note.Document!;
        byte[] cipher = f.Cipher(); string metadata = JsonSerializer.Serialize(note.Metadata); var objects = initial.AttachmentObjects;
        var context = f.Capture(); var competing = f.Capture();
        Require(!context.GetType().IsPublic && context.GetType().GetConstructors().Length == 0, "Context has no public constructor");
        Require(ReferenceEquals(Property(context, "OriginalDocument"), source), "Exact original document identity");
        int changes = 0; f.Owner.Workspace.Changed += () => changes++;
        var noChange = f.Apply(context, new(2, source.SourceJson));
        Require(Outcome(noChange) == "NoChange" && changes == 0 && !f.Owner.IsDirty && f.Current(Continuation(noChange)!), "No-op remains exact clean source and yields fresh single-use context");
        Require(!f.Current(context) && Outcome(f.Apply(context, f.Edit("reuse"))) == "NotApplied", "No-op predecessor consumed");
        context = Continuation(noChange)!;
        long version = note.EditVersion, content = note.ContentVersion, epoch = f.Owner.AttachmentPreviewEpoch;
        bool invalidationRan = false, oldObserved = false, witness = false;
        Action<NoteDraft?> invalidation = n =>
        {
            invalidationRan = true; oldObserved = ReferenceEquals(n, note) && ReferenceEquals(note.Document, source) && note.EditVersion == version;
            witness = (bool)Call(f.Owner, "IsRichImageTextEditOwnTransition", context, note.Document!, note.EditVersion, note.ContentVersion, f.Owner.AttachmentPreviewEpoch)!;
        };
        f.Owner.Workspace.AttachmentReadInvalidating += invalidation;
        var first = f.Apply(context, f.Edit("한글 😀 first"));
        f.Owner.Workspace.AttachmentReadInvalidating -= invalidation;
        Require(invalidationRan && oldObserved && witness, "Invalidation sees intact old fields and exact single own transition");
        Require(Outcome(first) == "AppliedDirty" && (bool)Property(first, "NotificationSucceeded")! && changes == 1,
            $"One complete dirty publication (observed {changes})");
        Require(note.EditVersion == version + 1 && note.ContentVersion == content + 1 && f.Owner.AttachmentPreviewEpoch == epoch + 1,
            "Exact own version/content/epoch increments");
        Require(!f.Current(competing) && f.Current(Continuation(first)!), "Other projection stale, exact own continuation current");
        Require(f.Owner.IsDirty && cipher.SequenceEqual(f.Cipher()) && metadata == JsonSerializer.Serialize(note.Metadata)
            && objects.SequenceEqual(f.Owner.Workspace.Capture().AttachmentObjects), "Dirty text changes no cipher/metadata/objects");
        var second = f.Apply(Continuation(first)!, new(2, note.Document!.SourceJson.Replace("한글 😀 first", "second")));
        Require(Outcome(second) == "AppliedDirty" && changes == 2, "Continued typing has one notification per transaction");
        var dirty = f.Owner.Workspace.Capture();
        Require(dirty.History.Count(r => r.RevisionId == initial.Notes.Single(n => n.NoteId == note.Id).RevisionId) == 1
            && dirty.History.Length == initial.History.Length + 1, "Multiple dirty keystrokes retain accepted predecessor once");
        var undo = f.Apply(Continuation(second)!, source);
        Require(Outcome(undo) == "AppliedDirty" && ReferenceEquals(note.Document, source) && note.Document.SourceJson == source.SourceJson, "Exact source undo uses owned original object");
        var styled = StyledAroundImages(source);
        var styledReceipt = f.Apply(Continuation(undo)!, styled);
        Require(Outcome(styledReceipt) == "AppliedDirty" && RichDocumentCodec.Images(note.Document!).Length == 3, "Lists checklist table styles and surrounding paragraphs retain repeated image sequence");
        Require(f.Owner.SaveAsync().GetAwaiter().GetResult(), "Canonical edited v2 save");
        var saved = f.Owner.Workspace.Capture();
        Require(saved.Notes.Single(n => n.NoteId == note.Id).Document == styled
            && saved.History.Single(r => r.RevisionId == initial.Notes.Single(n => n.NoteId == note.Id).RevisionId).Document == source,
            "Saved exact old source and edited current source");
        Fails(() => f.Owner.Workspace.SetRichDocument(note, f.Edit("generic")), "Generic v2 editing still refuses");
    }
    private static int IssuedCount(SaveCoordinator owner) =>
        ((System.Collections.IDictionary)typeof(SaveCoordinator).GetField("richImageTextEntries", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!).Count;
    private static void RegistryDrop()
    {
        foreach (string trigger in new[] { "metadata", "lock", "dispose" })
        {
            using var f = new Fixture();
            var first = f.Capture(); var unmounted = f.Capture();
            Require(IssuedCount(f.Owner) == 2, "Two genuinely issued mounted/unmounted contexts");
            if (trigger == "metadata") f.Note.Important = true;
            if (trigger == "lock") f.Owner.LockAsync().GetAwaiter().GetResult();
            if (trigger == "dispose") f.Owner.Dispose();
            Require(IssuedCount(f.Owner) == 0 && !f.Current(first) && !f.Current(unmounted),
                "Owner registry drops every revoked non-in-flight source reference on " + trigger);
        }
    }
    private static void RefusedRegistryDrop()
    {
        bool staleClean=false,occupiedClean=false;int staleCount=-1,occupiedCount=-1;
        using(var f=new Fixture())
        {
            var context=f.Capture();var source=f.Note.Document;byte[] cipher=f.Cipher();
            Call(f.Vault,"RevokeAttachmentUse"); // No workspace event: exact stale-root refusal branch.
            var result=f.Apply(context,f.Edit("refused root"));staleCount=IssuedCount(f.Owner);
            staleClean=Outcome(result)=="NotApplied"&&staleCount==0&&!f.Current(context)
                &&ReferenceEquals(f.Note.Document,source)&&cipher.SequenceEqual(f.Cipher());
        }
        using(var f=new Fixture())
        {
            var actual=f.Capture();var competing=f.Capture();
            bool observed=false, inside=false, refused=false, onlyActual=false, witness=false;
            f.OwnerProbe=()=>
            {
                if(observed||inside||typeof(SaveCoordinator).GetField("richImageTextAttempt",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Owner) is null)return;
                observed=true;inside=true;
                try
                {
                    refused=Outcome(f.Apply(competing,f.Edit("competing must refuse")))=="NotApplied";
                    occupiedCount=IssuedCount(f.Owner);onlyActual=occupiedCount==1&&!f.Current(competing);
                    witness=(bool)Call(f.Owner,"IsRichImageTextEditOwnTransition",actual,f.Note.Document!,f.Note.EditVersion,f.Note.ContentVersion,f.Owner.AttachmentPreviewEpoch)!;
                }
                finally{inside=false;}
            };
            var result=f.Apply(actual,f.Edit("actual in-flight edit"));f.OwnerProbe=null;
            occupiedClean=observed&&refused&&onlyActual&&witness;
            Require(Outcome(result)=="AppliedDirty"&&f.Current(Continuation(result)!)&&IssuedCount(f.Owner)==1,
                "Real attempted edit still publishes and owns only the newly issued continuation");
        }
        Require(staleClean&&occupiedClean,$"Every early refusal drops only its own consumed entry: stale-root count {staleCount} expected0; occupied count {occupiedCount} expected1");
    }
    private static StyledDocument StyledAroundImages(StyledDocument source)
    {
        using var parsed = JsonDocument.Parse(source.SourceJson); string[] images = parsed.RootElement.GetProperty("nodes").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "image").Select(n => n.GetRawText()).ToArray();
        return new(2, "{\"nodes\":[" + images[0] + ",{\"type\":\"paragraph\",\"runs\":[{\"text\":\"bold text\",\"bold\":true}]}," + images[1]
            + ",{\"type\":\"list\",\"ordered\":false,\"items\":[{\"runs\":[{\"text\":\"list\"}]}]},{\"type\":\"checklist\",\"items\":[{\"checked\":true,\"runs\":[{\"text\":\"check\"}]}]},{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"cell\"}]}]]}," + images[2] + "]}");
    }
    private static void ImageRefusals()
    {
        using var f = new Fixture(); var source = f.Note.Document!; using var parsed = JsonDocument.Parse(source.SourceJson);
        string[] nodes = parsed.RootElement.GetProperty("nodes").EnumerateArray().Select(n => n.GetRawText()).ToArray();
        var candidates = new List<StyledDocument>
        {
            new(1, "{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"down\"}]}]}"),
            new(2, source.SourceJson.Replace(f.Image.ToString(), Guid.NewGuid().ToString())),
            new(2, source.SourceJson.Replace("\"first\"", "\"changed\"")),
            new(2, "{\"nodes\":[" + string.Join(',', nodes.Skip(1)) + "]}"),
            new(2, "{\"nodes\":[" + string.Join(',', nodes.Concat([nodes[0]])) + "]}"),
            new(2, "{\"nodes\":[" + string.Join(',', new[] {nodes[3],nodes[1],nodes[2],nodes[0]}) + "]}"),
            new(2, source.SourceJson.Replace("\"type\":\"image\"", "\"type\" : \"image\"")),
            new(2, "{\"nodes\":[{\"type\":\"unknown\"}]}"),
            new(2, source.SourceJson.Replace("FIRST", new string('x',65537))),
            new(2, new string(' ',RichDocumentCodec.MaxSourceBytes)+source.SourceJson),
            new(2, "{\"nodes\":["+string.Join(',',nodes.Where(n=>n.Contains("\"image\"",StringComparison.Ordinal)).Concat(Enumerable.Repeat("{\"type\":\"paragraph\",\"runs\":[]}",1023)))+"]}"),
            new(2, "{\"nodes\":["+string.Join(',',nodes.Where(n=>n.Contains("\"image\"",StringComparison.Ordinal)))+",{\"type\":\"paragraph\",\"runs\":["+string.Join(',',Enumerable.Repeat("{\"text\":\"\"}",4097))+"]}]}")
        };
        byte[] cipher = f.Cipher(); long version = f.Note.EditVersion;
        foreach (var candidate in candidates)
        {
            var context = f.Capture(); Require(Outcome(f.Apply(context,candidate)) == "NotApplied" && !f.Current(context), "Whole unsupported or immutable image candidate refusal consumes context");
            Require(ReferenceEquals(f.Note.Document,source) && f.Note.EditVersion == version && !f.Owner.IsDirty && cipher.SequenceEqual(f.Cipher()), "Rejected candidate leaves exact source/cipher clean");
        }
    }
    private static void Authority()
    {
        using var f = new Fixture(); var context = f.Capture(); var original = f.Note.Document!;
        foreach (Action action in new Action[] { () => f.Capture(), () => f.Current(context), () => f.Apply(context,f.Edit("foreign")),
            () => Call(f.Owner,"RetireRichImageTextEditContext",context), () => Call(f.Owner,"RegisterRichImageTextEditOwner",(Func<bool>)(()=>true)) })
        {
            Exception? error = null; var thread = new Thread(() => { try { action(); } catch(Exception ex) { error=ex; } });
            thread.Start(); Require(thread.Join(TimeSpan.FromSeconds(10)),"Foreign thread settled");
            Require(error is InvalidOperationException && f.Current(context) && ReferenceEquals(f.Note.Document,original),"Real foreign thread rejected before registry/source mutation");
        }
        using (var other = new Fixture()) Require(Outcome(other.Apply(context,other.Edit("foreign issuer"))) == "NotApplied" && f.Current(context),"Wrong coordinator cannot consume original issuer authority");
        var second = f.Owner.Workspace.Duplicate(f.Note); Require(!f.Current(context),"Other note transaction invalidates original global epoch");
        context = f.Capture(); f.Owner.Workspace.AcceptPrepared(f.Owner.Workspace.Capture());
        Require(!f.Current(context) && Outcome(f.Apply(context,f.Edit("old"))) == "NotApplied", "Same-version AcceptPrepared retires source context");
        context = f.Capture(second); Call(f.Owner,"RetireRichImageTextEditContext",context); Call(f.Owner,"RetireRichImageTextEditContext",context);
        Require(!f.Current(context) && Outcome(f.Apply(context,second.Document!)) == "NotApplied","Retirement is permanent idempotent exact context");
        context = f.Capture(); f.Note.Important=true;
        Require(!f.Current(context) && Outcome(f.Apply(context,f.Edit("metadata stale"))) == "NotApplied","Metadata invalidates original context without content renewal");
        context = f.Capture(); f.Owner.LockAsync().GetAwaiter().GetResult();
        Require(!f.Current(context) && Outcome(f.Apply(context,original)) == "NotApplied","Lock revokes and conceals source authority");
    }
    private static void Reentry()
    {
        foreach (string kind in new[] {"retire","metadata","accept","other"})
        {
            using var f = new Fixture(); var other = f.Owner.Workspace.CreateNote(); Require(f.Owner.SaveAsync().GetAwaiter().GetResult(),"Reentry source clean");
            var context = f.Capture(); var original=f.Note.Document!; long version=f.Note.EditVersion; bool ran=false, observed=false;
            Action<NoteDraft?> handler = _ =>
            {
                ran=true; observed=ReferenceEquals(f.Note.Document,original)&&f.Note.EditVersion==version;
                switch(kind) { case "retire": Call(f.Owner,"RetireRichImageTextEditContext",context);break;
                    case "metadata": f.Note.Favorite=true;break; case "accept": f.Owner.Workspace.AcceptPrepared(f.Owner.Workspace.Capture());break;case "other": other.Title="external edit";break; }
            };
            // Remove before nested mutations to avoid recursive fixture handlers; the recorder assertions are outside Apply.
            Action<NoteDraft?>? once=null; once=n=>{f.Owner.Workspace.AttachmentReadInvalidating-=once;handler(n);};
            f.Owner.Workspace.AttachmentReadInvalidating+=once;
            var receipt=f.Apply(context,f.Edit("must not install"));
            Require(ran&&observed&&Outcome(receipt)=="NotApplied"&&ReferenceEquals(f.Note.Document,original),"Prepublication "+kind+" reentry refuses without overwriting source");
        }
        foreach(string kind in new[]{"throw","edit","retire"})
        {
            using var f=new Fixture();var context=f.Capture();bool ran=false;int changes=0; f.Owner.Workspace.Changed+=()=>changes++;
            Action? observer=null;observer=()=>{f.Owner.Workspace.Changed-=observer;ran=true;
                if(kind=="throw")throw new InvalidOperationException("synthetic observer");
                if(kind=="edit")f.Note.Title="newer real edit";
                if(kind=="retire")Call(f.Owner,"RetireRichImageTextEditContext",context);};
            f.Owner.Workspace.Changed+=observer;
            var receipt=f.Apply(context,f.Edit("installed"));
            Require(ran&&Outcome(receipt)=="AppliedDirty"&&Continuation(receipt) is null&&f.Note.Text.Contains("installed")&&f.Owner.IsDirty,"Postinstallation "+kind+" reports truthful applied dirty without continuation");
            Require(changes==(kind=="edit"?2:1),"Actual reentrant edit retains normal dirty notification");
            if(kind=="edit")Require(f.Note.Title=="newer real edit","No predecessor restore over newer edit");
        }
    }
    private static void Budgets()
    {
        using var f=new Fixture();var snapshot=f.Owner.Workspace.Capture();var head=snapshot.Notes.Single(n=>n.NoteId==f.Note.Id);
        var history=snapshot.History.Concat(Enumerable.Range(0,512-snapshot.History.Length).Select(_=>Revision(head with{RevisionId=Guid.NewGuid(),Parents=[]}))).ToArray();
        f.Reload(snapshot with{History=history});var context=f.Capture();var original=f.Note.Document!;
        Require(Outcome(f.Apply(context,f.Edit("history overflow")))=="NotApplied"&&ReferenceEquals(original,f.Note.Document),"Full retained 512 history duplication refuses instead of pruning");
        // Reload the exact complete source (including required parent relations), never stage metadata
        // via AcceptPrepared while leaving live fields different from the snapshot under test.
        f.Reload(snapshot);
        var obj=snapshot.AttachmentObjects[0];
        foreach(bool aggregate in new[]{false,true})
        {
            var withOcr=head with{Metadata=head.Metadata with{AttachmentOcrResults=[Ocr(obj,aggregate?new string('한',65536):"synthetic OCR")]}};
            var rows=Enumerable.Range(0,aggregate?4:127).Select(_=>Revision(withOcr with{RevisionId=Guid.NewGuid(),Parents=[]})).ToArray();
            var crowded=snapshot with{SchemaVersion=11,Notes=[withOcr],History=snapshot.History.Concat(rows).ToArray()};VaultEnvelope.Validate(crowded);f.Reload(crowded);
            original=f.Note.Document!;
            context=f.Capture(); Require(Outcome(f.Apply(context,f.Edit("OCR duplication overflow")))=="NotApplied"&&ReferenceEquals(original,f.Note.Document),"Schema11 "+(aggregate?"UTF8 aggregate":"128 occurrence")+" historical duplication budget refusal");
        }
    }
    private static void ManifestAuthority()
    {
        using(var f=new Fixture(false))Fails(()=>f.Capture(),"Unsaved newly encrypted descriptor has no prior-authenticated manifest authority");
        using(var f=new Fixture())
        {
            var snapshot=f.Owner.Workspace.Capture();var item=snapshot.AttachmentObjects[0];
            Require((bool)Call(f.Vault,"IsKnownAuthenticatedAttachmentDescriptor",item)!,"Prior-authenticated descriptor predicate");
            var changed=item with{Name="forged.png"};
            f.Owner.Workspace.AcceptPrepared(snapshot with{AttachmentObjects=[changed]});
            Fails(()=>f.Capture(),"Caller AcceptPrepared forged object metadata is not prior authenticated");
            Require(!(bool)Call(f.Vault,"IsKnownAuthenticatedAttachmentDescriptor",changed)!,"Manifest predicate compares complete immutable fields");
            f.Owner.Workspace.AcceptPrepared(snapshot);
            Call(f.Owner.Workspace,"RemoveInlineImage",f.Note,3);Call(f.Owner.Workspace,"RemoveInlineImage",f.Note,2);Call(f.Owner.Workspace,"RemoveInlineImage",f.Note,0);
            Require(f.Note.Document!.SchemaVersion==2&&RichDocumentCodec.Images(f.Note.Document).Length==0,"Zero-image stays v2");
            var context=f.Capture();Require(Outcome(f.Apply(context,new(2,f.Note.Document.SourceJson.Replace("FIRST","zero image edited"))))=="AppliedDirty","Actual-root zero-image v2 text edit");
            var zero=f.Owner.Workspace.Capture();Guid foreign=Guid.NewGuid();
            f.Owner.Workspace.AcceptPrepared(zero with{AttachmentRootId=foreign,AttachmentObjects=zero.AttachmentObjects.Select(o=>o with{RootId=foreign}).ToImmutableArray()});
            Fails(()=>f.Capture(),"Zero-image path cannot skip actual owned root proof");
            Call(f.Vault,"RevokeAttachmentUse");
            Require(!(bool)Call(f.Vault,"IsCurrentAnchoredAttachmentRoot",snapshot.AttachmentRootId)!
                &&!(bool)Call(f.Vault,"IsKnownAuthenticatedAttachmentDescriptor",item)!,"Use revocation rejects cached prior-authenticated facts");
        }
    }
    private static StoredAttachmentOcrResult Ocr(StoredAttachmentObject obj,string text)
    {
        // Historical stored facts, not a newly minted recognition result or execution proof.
        var provenance=new StoredOcrProvenance("db0ec62f81b0737fbbe184d8fea40af5738f8eef","13275a278eb55b5746e33f95fbf5a2c8f604b3ab","87416418657359cb625c412a48b6e1d6d41c29bd",new string('a',64),"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2","7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2","kor+eng",1,6,"png-nearest-1024-straight-alpha-white-ppm-v1",1,1,1,1,new string('b',64));
        return new(obj.ObjectId,obj.Sha256,text,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),provenance);
    }
    private static void PersistenceAndRecovery()
    {
        using var f=new Fixture();var initial=f.Owner.Workspace.Capture();var old=initial.Notes.Single(n=>n.NoteId==f.Note.Id);
        var exact=new StyledDocument(2," \n"+old.Document!.SourceJson.Replace("\"FIRST\"","\"F\\u0049RST\"",StringComparison.Ordinal)+" \n");
        var folder=new StoredFolder(Guid.NewGuid(),null,"synthetic folder");var tag=new StoredTag(Guid.NewGuid(),"synthetic tag");
        var metadata=old.Metadata with{FolderId=folder.FolderId,TagIds=[tag.TagId],Favorite=true,Color="blue",
            AttachmentOcrResults=[Ocr(initial.AttachmentObjects[0],"historical synthetic OCR 한글")]};
        var original=old with{RevisionId=Guid.NewGuid(),Parents=[old.RevisionId],Document=exact,Metadata=metadata};
        f.Reload(initial with{SchemaVersion=11,Notes=[original],History=initial.History.Append(Revision(old)).ToArray(),Folders=[folder],Tags=[tag]});
        byte[] cipher=f.Cipher();var before=f.Owner.Workspace.Capture();string objects=JsonSerializer.Serialize(before.AttachmentObjects),facts=JsonSerializer.Serialize(f.Note.Metadata);
        var context=f.Capture();var edited=new StyledDocument(2,exact.SourceJson.Replace("F\\u0049RST","EDITED 한글 😀",StringComparison.Ordinal));
        var result=f.Apply(context,edited);
        Require(Outcome(result)=="AppliedDirty"&&ReferenceEquals(f.Note.Document,edited)&&facts==JsonSerializer.Serialize(f.Note.Metadata)
            &&objects==JsonSerializer.Serialize(f.Owner.Workspace.Capture().AttachmentObjects)&&before.AttachmentRootId==f.Owner.Workspace.Capture().AttachmentRootId
            &&cipher.SequenceEqual(f.Cipher())&&f.Owner.WhenAttachmentReadsIdle.IsCompleted,"Typing preserves all stored OCR/organization/object/root fields and original ciphertext");
        Require(f.Owner.SaveAsync().GetAwaiter().GetResult(),"Edited schema11 source actual encrypted save");
        byte[] editedBackup=f.Cipher();var oldContinuation=Continuation(result)!;Guid noteId=f.Note.Id;
        f.Reopen();
        Require(!f.Current(oldContinuation)&&Outcome(f.Apply(oldContinuation,edited))=="NotApplied","Reopened same UUID/root cannot replay former issuer context");
        var reopened=f.Owner.Workspace.Capture();
        Require(f.Note.Document!.SourceJson==edited.SourceJson&&facts==JsonSerializer.Serialize(f.Note.Metadata)
            &&objects==JsonSerializer.Serialize(reopened.AttachmentObjects)&&reopened.SchemaVersion==11
            &&reopened.Folders.Single()==folder&&reopened.Tags.Single()==tag
            &&reopened.History.Single(r=>r.RevisionId==original.RevisionId).Document!.SourceJson==exact.SourceJson,
            "Actual reopen retains exact edited source, predecessor whitespace/escape source, metadata/OCR/organization and immutable objects");
        byte[] png=f.Owner.ReadAttachmentBytes(f.Note,f.Image,f.Note.EditVersion);
        try{Require(Convert.ToHexStringLower(SHA256.HashData(png))==reopened.AttachmentObjects.Single().Sha256,"Explicit separate authenticated payload read proves unchanged original object after save/reopen");}
        finally{CryptographicOperations.ZeroMemory(png);}
        context=f.Capture();f.Owner.Workspace.RestoreRevision(f.Note,original.RevisionId);
        Require(!f.Current(context)&&f.Note.Document!.SourceJson==exact.SourceJson&&facts==JsonSerializer.Serialize(f.Note.Metadata)
            &&f.Owner.Workspace.Capture().History.Any(r=>r.Document?.SourceJson==edited.SourceJson),"History restore revokes edit context, restores exact original source and retains edited predecessor");
        Require(f.Owner.SaveAsync().GetAwaiter().GetResult(),"Restored complete source save");
        context=f.Capture();var imported=f.Owner.ImportSelectedEncryptedBackup(editedBackup,[noteId],f.Owner.AttachmentPreviewEpoch).Single();
        Require(!f.Current(context)&&imported.Id!=noteId&&imported.Document!.SourceJson==edited.SourceJson
            &&imported.Metadata.AttachmentOcrResults.SequenceEqual(f.Note.Metadata.AttachmentOcrResults)
            &&f.Note.Document!.SourceJson==exact.SourceJson&&objects==JsonSerializer.Serialize(f.Owner.Workspace.Capture().AttachmentObjects),
            "Authenticated selected backup import preserves images/OCR source under fresh note identity without changing original body or objects");
        var importedContext=f.Capture(imported);
        var importedResult=f.Apply(importedContext,new(2,imported.Document!.SourceJson.Replace("EDITED","IMPORTED",StringComparison.Ordinal)));
        Require(Outcome(importedResult)=="AppliedDirty"&&f.Note.Document.SourceJson==exact.SourceJson,"Shared-object imported note editing remains note scoped");
        Require(f.Owner.SaveAsync().GetAwaiter().GetResult(),"Imported edited source saved");
        var hiddenDocument=new StyledDocument(2,f.Note.Document.SourceJson.Replace("F\\u0049RST","HIDDEN edited",StringComparison.Ordinal));
        context=f.Capture();result=f.Apply(context,hiddenDocument);Require(Outcome(result)=="AppliedDirty","Hidden-recovery source starts with real dirty text edit");
        var stale=Continuation(result)!;cipher=f.Cipher();f.Note.Title=new string('t',257);
        Require(!f.Owner.LockAsync().GetAwaiter().GetResult()&&f.Owner.PendingKind=="plaintext-hidden"&&!f.Owner.KeysReleased
            &&IssuedCount(f.Owner)==0&&!f.Current(stale)&&cipher.SequenceEqual(f.Cipher()),"Repairable existing title fault hides dirty source, drops issuer registry and preserves current ciphertext");
        f.Owner.ResumeHidden(f.Secret);f.Note=f.Owner.Workspace.Notes.Single(n=>n.Id==noteId);
        Require(f.Note.Title.Length==257&&f.Note.Document!.SourceJson==hiddenDocument.SourceJson
            &&facts==JsonSerializer.Serialize(f.Note.Metadata)&&objects==JsonSerializer.Serialize(f.Owner.Workspace.Capture().AttachmentObjects)
            &&!f.Current(stale)&&Outcome(f.Apply(stale,edited))=="NotApplied","Hidden recovery keeps source/OCR/root objects but old workspace/session cannot replay");
        var invalidTitleContext=f.Capture();
        Require(Outcome(f.Apply(invalidTitleContext,new(2,hiddenDocument.SourceJson.Replace("HIDDEN","REFUSED",StringComparison.Ordinal))))=="NotApplied"
            &&f.Note.Document!.SourceJson==hiddenDocument.SourceJson,"Full candidate title validation refuses while existing hidden draft remains repairable");
        f.Note.Title="synthetic repaired";context=f.Capture();
        var repaired=new StyledDocument(2,hiddenDocument.SourceJson.Replace("HIDDEN","REPAIRED",StringComparison.Ordinal));
        Require(Outcome(f.Apply(context,repaired))=="AppliedDirty"&&f.Owner.SaveAsync().GetAwaiter().GetResult(),"Explicit repair and fresh authority save resumed edited source");
        f.Reopen();Require(f.Note.Document!.SourceJson==repaired.SourceJson&&facts==JsonSerializer.Serialize(f.Note.Metadata)
            &&objects==JsonSerializer.Serialize(f.Owner.Workspace.Capture().AttachmentObjects),"Repaired encrypted reopen retains exact source and original OCR/object facts");
    }
    private static StoredRevision Revision(StoredNote note)=>new(note.NoteId,note.RevisionId,note.Parents,note.ModifiedAt,note.Title,note.Text){Metadata=note.Metadata,Mode=note.Mode,Document=note.Document,AttachmentIds=note.AttachmentIds};
    private static string Outcome(object receipt)=>Property(receipt,"Outcome")!.ToString()!;
    private static object? Continuation(object receipt)=>Property(receipt,"Continuation");
    private static object? Property(object obj,string name)=>obj.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(obj);
    private static object? Call(object target,string name,params object?[] args)
    {try{return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(target,args);}catch(TargetInvocationException error) when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    private static void Fails(Action action,string message){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}catch(InvalidDataException){failed=true;}Require(failed,message);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}

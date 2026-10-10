using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Editing;

internal enum AttachmentOcrApplyOutcome { Rejected, AppliedDirty }
public sealed partial class SaveCoordinator
{
    private sealed class OcrGrantEntry(AttachmentOcrReadGrant grant,NoteDraft note,StoredAttachmentObject source)
    {
        internal readonly AttachmentOcrReadGrant Grant=grant;
        internal readonly NoteDraft Note=note;
        internal readonly StoredAttachmentObject Object=source;
        internal bool Running,Revoked,Consumed;
        internal LinkedOcrOperation? Operation;
        internal bool Settled=>!Running||(Operation is { } op&&op.Settled.IsCompletedSuccessfully&&op.Completion.IsCompleted);
    }
    private readonly Dictionary<Guid,OcrGrantEntry> ocrGrants=new();
    private Guid ocrSessionNonce=Guid.NewGuid();
    private long ocrNonceEpoch;
    private Func<bool>? ocrCheckAccess;
    private OcrGrantEntry? ocrApplyPermit;
    private bool ocrOwnRevocation;
    private void EnsureOcrOwner()
    {if(ocrCheckAccess?.Invoke()!=true)throw new InvalidOperationException("OCR owner access required");}
    private void RequireOcrOpen()
    {EnsureOcrOwner();if(disposed||IsLocked||vault.IsFaulted||vault.KeysReleased)throw new InvalidOperationException("OCR session ended");}
    internal AttachmentOcrReadGrant CreateAttachmentOcrReadGrant(NoteDraft note,Guid id,long expectedVersion,long expectedPreviewEpoch,Func<bool> checkAccess)
    {
        ArgumentNullException.ThrowIfNull(checkAccess);
        if(ocrCheckAccess is null){if(!checkAccess())throw new InvalidOperationException("OCR owner access required");ocrCheckAccess=checkAccess;}
        RequireOcrOpen();if(!checkAccess())throw new InvalidOperationException("OCR caller owner access required");
        // Admission precedes source authentication, read-slot allocation and plaintext allocation.
        if(ocrGrants.Count>=4)throw new InvalidOperationException("Four outstanding OCR grants already issued");
        if(expectedPreviewEpoch!=AttachmentPreviewEpoch)throw new InvalidOperationException("OCR preview changed");
        RequireAttachmentSource(note,expectedVersion,sessionEpoch);
        if(!vault.AttachmentRootAnchored)throw new InvalidOperationException("OCR needs an authenticated committed attachment root");
        var item=Workspace.AttachmentObject(note,id);
        if(item.Mime!="image/png")throw new InvalidOperationException("OCR PNG source required");
        var source=new OcrSourceDescriptor(item.ObjectId,item.RootId,item.Sha256,item.Length);source.Validate();
        if(ocrNonceEpoch!=sessionEpoch){ocrSessionNonce=Guid.NewGuid();ocrNonceEpoch=sessionEpoch;}
        var stamp=new OcrGrantStamp(ocrSessionNonce,Guid.NewGuid(),note.Id,expectedVersion,expectedPreviewEpoch);
        ocrGrants.EnsureCapacity(4);
        AttachmentReadLease? lease=null;
        try
        {
            lease=CreateAttachmentReadLease(note,id,expectedVersion);
            RequireOcrOpen();if(expectedPreviewEpoch!=AttachmentPreviewEpoch||!ReferenceEquals(item,Workspace.AttachmentObject(note,id)))throw new InvalidOperationException("OCR source changed");
            var grant=AttachmentOcrReadGrant.Issued(lease,source,stamp);
            ocrGrants.Add(stamp.GrantId,new(grant,note,item));lease=null;return grant;
        }
        finally{lease?.Dispose();}
    }
    private OcrGrantEntry RequireOcrGrant(AttachmentOcrReadGrant grant)
    {
        EnsureOcrOwner();ArgumentNullException.ThrowIfNull(grant);
        if(!ocrGrants.TryGetValue(grant.Stamp.GrantId,out var entry)||!ReferenceEquals(entry.Grant,grant)||entry.Revoked||entry.Consumed)throw new InvalidOperationException("OCR issued grant ended");
        return entry;
    }
    // The detached starter allocates its operation before launch. No arbitrary launch callback exists.
    internal LinkedOcrOperation StartAttachmentOcrGrant(AttachmentOcrReadGrant grant,LinkedOcrStarter starter)
    {
        RequireOcrOpen();ArgumentNullException.ThrowIfNull(starter);
        if(!Equals(starter.Source,grant.Source)||!Equals(starter.Stamp,grant.Stamp))throw new InvalidOperationException("OCR starter source differs from issued grant");
        BindAttachmentOcrOperation(grant,starter.Operation);
        try{starter.Start();return starter.Operation;}
        catch{RetireAttachmentOcrGrant(grant);throw;}
    }
    private void BindAttachmentOcrOperation(AttachmentOcrReadGrant grant,LinkedOcrOperation operation)
    {
        RequireOcrOpen();ArgumentNullException.ThrowIfNull(operation);var entry=RequireOcrGrant(grant);
        CheckOcrSource(entry,entry.Note,grant.Source.ObjectId,grant.Stamp.Version,grant.Stamp.PreviewEpoch,false);
        if(entry.Running)throw new InvalidOperationException("OCR grant already started");
        entry.Operation=operation;entry.Running=true;
    }
    internal void RetireAttachmentOcrGrant(AttachmentOcrReadGrant grant)
    {
        EnsureOcrOwner();ArgumentNullException.ThrowIfNull(grant);
        if(ocrApplyPermit is { } applying&&ReferenceEquals(applying.Grant,grant)){applying.Revoked=true;DisposeOcrResult(applying);}
        if(!ocrGrants.TryGetValue(grant.Stamp.GrantId,out var entry)||!ReferenceEquals(entry.Grant,grant))return;
        entry.Revoked=true;entry.Grant.Lease.Dispose();DisposeOcrResult(entry);if(entry.Settled)ocrGrants.Remove(grant.Stamp.GrantId);
    }
    internal void SettleAttachmentOcrGrant(AttachmentOcrReadGrant grant)
    {
        EnsureOcrOwner();ArgumentNullException.ThrowIfNull(grant);
        if(!ocrGrants.TryGetValue(grant.Stamp.GrantId,out var entry)||!ReferenceEquals(entry.Grant,grant))return;
        if(!entry.Settled)throw new InvalidOperationException("OCR worker and result have not settled");
        if(entry.Revoked||entry.Consumed){DisposeOcrResult(entry);ocrGrants.Remove(grant.Stamp.GrantId);}
    }
    private static void DisposeOcrResult(OcrGrantEntry entry)
    {if(entry.Operation is { } op&&op.Completion.IsCompletedSuccessfully)op.Completion.Result.Dispose();}
    private void RevokeOcrGrants()
    {
        if(ocrCheckAccess is not null)EnsureOcrOwner();
        if(ocrApplyPermit is { } permit&&!ocrOwnRevocation&&Workspace.IsOcrPublicationActive)
            ocrOwnRevocation=true;
        else if(ocrApplyPermit is { } invalid)invalid.Revoked=true;
        foreach(var pair in ocrGrants.ToArray())
        {var entry=pair.Value;entry.Revoked=true;entry.Grant.Lease.Dispose();DisposeOcrResult(entry);if(entry.Settled)ocrGrants.Remove(pair.Key);}
    }
    private void CheckOcrSource(OcrGrantEntry entry,NoteDraft note,Guid id,long version,long epoch,bool applying)
    {
        RequireOcrOpen();var grant=entry.Grant;var stamp=grant.Stamp;
        long allowed=epoch+(applying&&ocrOwnRevocation?1:0);
        if(entry.Revoked||stamp.SessionNonce!=ocrSessionNonce||ocrNonceEpoch!=sessionEpoch||stamp.NoteId!=note.Id||!ReferenceEquals(entry.Note,note)||stamp.Version!=version||stamp.PreviewEpoch!=epoch||AttachmentPreviewEpoch!=allowed||grant.Source.ObjectId!=id)throw new InvalidOperationException("OCR original issuer or source changed");
        RequireAttachmentSource(note,version,sessionEpoch);
        var obj=Workspace.AttachmentObject(note,id);
        if(!ReferenceEquals(obj,entry.Object)||obj.RootId!=grant.Source.RootId||obj.Sha256!=grant.Source.Sha256||obj.Length!=grant.Source.Length||!vault.AttachmentRootAnchored)throw new InvalidOperationException("OCR authenticated source changed");
    }
    internal AttachmentOcrApplyOutcome ApplyAttachmentOcrResult(NoteDraft note,Guid id,long expectedVersion,long expectedPreviewEpoch,OwnedOcrDerivation result)
    {
        ArgumentNullException.ThrowIfNull(result);OcrGrantEntry? entry=null;bool applied=false,installedPermit=false;
        try
        {
            EnsureOcrOwner();
            if(ocrApplyPermit is not null)throw new InvalidOperationException("OCR publication occupied");
            // Every attempt consumes its genuine entry, even if requested note or source is wrong.
            if(!ocrGrants.TryGetValue(result.Stamp.GrantId,out entry))return AttachmentOcrApplyOutcome.Rejected;
            entry.Consumed=true;
            if(entry.Settled)ocrGrants.Remove(result.Stamp.GrantId);else{entry.Revoked=true;return AttachmentOcrApplyOutcome.Rejected;}
            if(entry.Operation is not { } issued||!issued.Completion.IsCompletedSuccessfully||!ReferenceEquals(issued.Completion.Result,result)||!Equals(result.Stamp,entry.Grant.Stamp)||!Equals(result.Source,entry.Grant.Source))return AttachmentOcrApplyOutcome.Rejected;
            CheckOcrSource(entry,note,id,expectedVersion,expectedPreviewEpoch,false);result.Provenance.Validate();
            StoredAttachmentOcrResult? stored=null;
            bool consumed=result.ConsumeUtf8(bytes=>
            {
                string hash=Convert.ToHexStringLower(SHA256.HashData(bytes));if(hash!=result.TextSha256)throw new InvalidDataException("OCR exact output digest changed");
                string text=new UTF8Encoding(false,true).GetString(bytes);var p=result.Provenance;
                stored=new(id,result.Source.Sha256,text,hash,new(OcrProvenanceFacts.TesseractCommit,OcrProvenanceFacts.LeptonicaCommit,OcrProvenanceFacts.ModelsCommit,p.EngineSha256,p.KorModelSha256,p.EngModelSha256,OcrProvenanceFacts.Languages,OcrProvenanceFacts.Oem,OcrProvenanceFacts.Psm,OcrProvenanceFacts.TransformProfile,p.SourceWidth,p.SourceHeight,p.PreviewWidth,p.PreviewHeight,p.PpmSha256));
            });
            if(!consumed||stored is null)return AttachmentOcrApplyOutcome.Rejected;
            ocrApplyPermit=entry;ocrOwnRevocation=false;installedPermit=true;
            Workspace.ApplyAttachmentOcrResult(note,stored,()=>{CheckOcrSource(entry,note,id,expectedVersion,expectedPreviewEpoch,true);return true;},()=>applied=true);
            return AttachmentOcrApplyOutcome.AppliedDirty;
        }
        catch when(applied){return AttachmentOcrApplyOutcome.AppliedDirty;}
        catch(InvalidOperationException){return AttachmentOcrApplyOutcome.Rejected;}
        catch(InvalidDataException){return AttachmentOcrApplyOutcome.Rejected;}
        finally{if(entry?.Consumed==true)DisposeOcrResult(entry);if(installedPermit){ocrApplyPermit=null;ocrOwnRevocation=false;}result.Dispose();}
    }
}

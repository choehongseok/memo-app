using MemoApp.Core.Transfer;
namespace MemoApp.Core.Editing;

// Detached worker input. Only the issuing coordinator's private registry grants publication authority.
internal sealed class AttachmentOcrReadGrant
{
    internal AttachmentReadLease Lease {get;}
    internal OcrSourceDescriptor Source {get;}
    internal OcrGrantStamp Stamp {get;}
    private AttachmentOcrReadGrant(AttachmentReadLease lease,OcrSourceDescriptor source,OcrGrantStamp stamp)
    {Lease=lease;Source=source;Stamp=stamp;}
    internal static AttachmentOcrReadGrant Issued(AttachmentReadLease lease,OcrSourceDescriptor source,OcrGrantStamp stamp)=>new(lease,source,stamp);
}

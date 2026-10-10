namespace MemoApp.Core.Transfer;

// Keyless values only. A stamp is not an issued grant or application authority.
internal sealed record OcrGrantStamp(Guid SessionNonce,Guid GrantId,Guid NoteId,long Version,long PreviewEpoch)
{
    internal void Validate(){if(SessionNonce==Guid.Empty||GrantId==Guid.Empty||NoteId==Guid.Empty||Version<0||PreviewEpoch<0)throw new InvalidDataException("OCR grant stamp");}
}
internal sealed record OcrSourceDescriptor(Guid ObjectId,Guid RootId,string Sha256,int Length)
{
    internal void Validate(){if(ObjectId==Guid.Empty||RootId==Guid.Empty||Length is <8 or >4194304||!OcrProvenanceFacts.Hash(Sha256))throw new InvalidDataException("OCR source descriptor");}
}
// Historical facts are not proof of execution. Only the verified runtime path creates a fresh owned result.
internal sealed record OcrProvenanceFacts(string EngineSha256,string KorModelSha256,string EngModelSha256,
    int SourceWidth,int SourceHeight,int PreviewWidth,int PreviewHeight,string PpmSha256)
{
    internal const string TesseractCommit="db0ec62f81b0737fbbe184d8fea40af5738f8eef";
    internal const string LeptonicaCommit="13275a278eb55b5746e33f95fbf5a2c8f604b3ab";
    internal const string ModelsCommit="87416418657359cb625c412a48b6e1d6d41c29bd";
    internal const string TransformProfile="png-nearest-1024-straight-alpha-white-ppm-v1";
    internal const string Languages="kor+eng";
    internal const int Oem=1,Psm=6;
    internal void Validate()
    {
        if(!Hash(EngineSha256)||!Hash(KorModelSha256)||!Hash(EngModelSha256)||!Hash(PpmSha256)||SourceWidth is <1 or >4096||SourceHeight is <1 or >4096||(long)SourceWidth*SourceHeight>4194304||PreviewWidth is <1 or >1024||PreviewHeight is <1 or >1024||(long)PreviewWidth*PreviewHeight>1048576)throw new InvalidDataException("OCR provenance facts");
        double scale=Math.Min(1.0,1024.0/Math.Max(SourceWidth,SourceHeight));
        if(PreviewWidth!=Math.Max(1,(int)Math.Floor(SourceWidth*scale))||PreviewHeight!=Math.Max(1,(int)Math.Floor(SourceHeight*scale)))throw new InvalidDataException("OCR transform dimensions");
    }
    internal static bool Hash(string value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

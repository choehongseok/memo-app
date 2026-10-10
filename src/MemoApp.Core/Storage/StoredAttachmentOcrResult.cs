namespace MemoApp.Core.Storage;
// Authenticated historical metadata, not an attestation or a fresh runtime OCR capability.
public sealed record StoredAttachmentOcrResult(Guid ObjectId,string SourceSha256,string Text,string TextSha256,StoredOcrProvenance Provenance);
public sealed record StoredOcrProvenance(string TesseractCommit,string LeptonicaCommit,string ModelsCommit,
    string EngineSha256,string KorModelSha256,string EngModelSha256,string Languages,int Oem,int Psm,
    string TransformProfile,int SourceWidth,int SourceHeight,int PreviewWidth,int PreviewHeight,string PpmSha256);

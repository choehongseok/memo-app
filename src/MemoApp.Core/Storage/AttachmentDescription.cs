namespace MemoApp.Core.Storage;
// Safe unlocked metadata projection. No filesystem path, key, ciphertext or content.
public sealed record AttachmentDescription(Guid Id,string Name,string Mime,int Length,string Sha256);

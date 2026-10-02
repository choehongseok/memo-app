using System.Text.Json;
namespace MemoApp.Core;

// Stage 0 wire contract only. No persistence, cryptography or networking exists yet.
public sealed record DocumentContract(
    int SchemaVersion,
    Guid NoteId,
    Guid RevisionId,
    Guid[] ParentRevisionIds,
    Guid DeviceId,
    string Title,
    string Mode,
    string Scope,
    bool Deleted,
    JsonElement Content,
    Guid[] AttachmentIds);

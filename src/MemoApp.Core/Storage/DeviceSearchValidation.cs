using MemoApp.Core.Documents;
namespace MemoApp.Core.Storage;
internal static class DeviceSearchValidation
{
    internal static void Validate(VaultSnapshot snapshot,StoredDeviceUi device)
    {
        if(device.RecentNoteIds.IsDefault||device.SavedSearches.IsDefault||device.RecentNoteIds.Length>20||device.SavedSearches.Length>20)throw new InvalidDataException("Search UI count/default limits");
        if(snapshot.SchemaVersion<6&&(device.RecentNoteIds.Length!=0||device.SavedSearches.Length!=0))throw new InvalidDataException("Older schema search UI state refused");
        var active=snapshot.Notes.Where(n=>!n.Metadata.Deleted).Select(n=>n.NoteId).ToHashSet();
        if(device.RecentNoteIds.Distinct().Count()!=device.RecentNoteIds.Length||device.RecentNoteIds.Any(id=>id==Guid.Empty||!active.Contains(id)))throw new InvalidDataException("Recent note references invalid");
        var ids=new HashSet<Guid>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var search in device.SavedSearches)
        {
            if(search is null||search.Id==Guid.Empty||!ids.Add(search.Id)||!Text(search.Name,64)||!names.Add(search.Name)||search.Options is not { } options)throw new InvalidDataException("Saved search identity/name limits");
            if(options.Query is null||options.Query.Length>256||options.Query.Any(char.IsControl)||!RichDocumentCodec.IsWellFormedUnicode(options.Query)||!Enum.IsDefined(options.Field)||!Enum.IsDefined(options.View)||!Enum.IsDefined(options.Sort)||options.FolderId==Guid.Empty||options.UnfiledOnly&&options.FolderId is not null||options.Tag is not null&&!Text(options.Tag,128)||options.ModifiedFrom is {} from&&from.Offset!=TimeSpan.Zero||options.ModifiedUntil is {} until&&until.Offset!=TimeSpan.Zero||options.ModifiedFrom is {} start&&options.ModifiedUntil is {} end&&start>=end)throw new InvalidDataException("Saved search condition limits");
            // A deleted folder leaves an inert exact reference. UI must refuse application before altering filters.
        }
    }
    private static bool Text(string value,int maximum)=>value is not null&&value.Length is >0&&value.Length<=maximum&&value.Trim()==value&&!value.Any(char.IsControl)&&RichDocumentCodec.IsWellFormedUnicode(value);
}

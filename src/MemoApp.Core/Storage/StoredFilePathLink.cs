using System.Collections.Immutable;
using MemoApp.Core.Documents;
using MemoApp.Core.Transfer;
namespace MemoApp.Core.Storage;
public sealed record StoredFilePathLink(Guid Id,Guid UiDeviceId,string Name,string Path);
public static class FilePathLink
{
    public static StoredFilePathLink Create(string path,Guid uiDeviceId)
    {
        if(path is null||path.Length>1024||uiDeviceId==Guid.Empty)throw new ArgumentException("Invalid file path/profile");
        path=LocalFilePath.Resolve(path);LocalFilePath.CheckAncestors(path,true);
        using var input=LocalRegularFile.Open(path); // Type/access only. Never read/copy file payload.
        var link=new StoredFilePathLink(Guid.NewGuid(),uiDeviceId,System.IO.Path.GetFileName(path),path);
        FilePathLinkValidation.Link(link);return link;
    }
    public static string ResolveForOpen(StoredFilePathLink link,Guid uiDeviceId)
    {
        ArgumentNullException.ThrowIfNull(link);
        // A foreign profile is refused before any filesystem lookup.
        if(link.UiDeviceId!=uiDeviceId||uiDeviceId==Guid.Empty)throw new IOException("File path belongs to another UI device profile");
        FilePathLinkValidation.Link(link);
        if(!System.IO.Path.IsPathFullyQualified(link.Path))throw new IOException("Stored path is not native to this platform");
        string path=LocalFilePath.Resolve(link.Path);
        if(path!=link.Path)throw new IOException("Stored path requires normalization");
        LocalFilePath.CheckAncestors(path,true);using var input=LocalRegularFile.Open(path);return path;
    }
}
internal static class FilePathLinkValidation
{
    internal static void Links(ImmutableArray<StoredFilePathLink> links,int schema)
    {
        if(links.IsDefault||links.Length>16||schema<7&&links.Length!=0||links.Select(l=>l?.Id).Distinct().Count()!=links.Length)throw new InvalidDataException("File path link count/schema/identity");
        foreach(var link in links)Link(link);
    }
    internal static void Link(StoredFilePathLink link)
    {
        if(link is null||link.Id==Guid.Empty||link.UiDeviceId==Guid.Empty||link.Name is null||link.Name.Length is <1 or >256||link.Path is null||link.Path.Length is <2 or >1024||!RichDocumentCodec.IsWellFormedUnicode(link.Name)||!RichDocumentCodec.IsWellFormedUnicode(link.Path)||link.Path.Any(char.IsControl)||link.Path.Contains('"'))throw new InvalidDataException("File path link scalar limits");
        bool windows=link.Path.Length>=4&&char.IsAsciiLetter(link.Path[0])&&link.Path[1]==':'&&link.Path[2]=='\\';
        string relative;
        if(windows){if(link.Path[2..].Any(c=>c is ':' or '/'))throw new InvalidDataException("Nonordinary Windows file path");relative=link.Path[3..];}
        else{if(link.Path[0]!='/'||link.Path.StartsWith("//",StringComparison.Ordinal)||link.Path.Any(c=>c is ':' or '\\'))throw new InvalidDataException("Nonordinary Unix file path");relative=link.Path[1..];}
        var segments=relative.Split(windows?'\\':'/');
        foreach(var segment in segments)
        {
            string stem=segment.Split('.')[0].ToUpperInvariant();
            if(segment.Length is <1 or >256||segment.Trim()!=segment||segment is "." or ".."||windows&&(segment.EndsWith('.')||segment.Any(c=>c is '<' or '>' or '|' or '?' or '*')||stem is "CON" or "PRN" or "AUX" or "NUL"||stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&"123456789¹²³".Contains(stem[3])))throw new InvalidDataException("Noncanonical/reserved file path segment");
        }
        if(link.Name!=segments[^1])throw new InvalidDataException("File link name/path mismatch");
    }
}

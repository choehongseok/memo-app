using System.Security.Cryptography;
using System.Text;
namespace MemoApp.Core.Transfer;
// Single raw UTF8 .md note only. No Obsidian plugins/settings/links/includes are imported or executed.
public static class MarkdownFileTransfer
{
    public static ImportedText Read(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();path=LocalFilePath.Resolve(path);if(!string.Equals(Path.GetExtension(path),".md",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Markdown source extension required");LocalFilePath.CheckAncestors(path,true);
        using var input=LocalRegularFile.Open(path);byte[] bytes=BoundedFileReader.Read(input,0,1024*1024,token);
        try
        {
            int bom=bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})?3:0;string raw=new UTF8Encoding(false,true).GetString(bytes.AsSpan(bom)),title=Path.GetFileNameWithoutExtension(path);
            if(raw.Length>65536||raw.Contains('\0')||title.Length>256)throw new InvalidDataException("Markdown content/title limit or binary source");token.ThrowIfCancellationRequested();
            return new(title,raw,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}

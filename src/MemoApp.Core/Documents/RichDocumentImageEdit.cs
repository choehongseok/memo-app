using System.Collections.Immutable;
using System.Text.Json;
namespace MemoApp.Core.Documents;
public static partial class RichDocumentCodec
{
    // Scalars describe placement only; callers must separately authenticate the active record's attachment authority.
    public static ImmutableArray<StoredInlineImage> Images(StyledDocument document)
    {
        var info=Inspect(document);if(document.SchemaVersion==1)return [];
        if(!info.Supported)throw new InvalidDataException("Unsupported image source");
        using var parsed=JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});var images=ImmutableArray.CreateBuilder<StoredInlineImage>();int index=0;
        foreach(var node in parsed.RootElement.GetProperty("nodes").EnumerateArray())
        {if(node.GetProperty("type").GetString()=="image")images.Add(new(index,Guid.ParseExact(node.GetProperty("attachmentId").GetString()!,"D"),node.GetProperty("alt").GetString()!));index++;}
        return images.ToImmutable();
    }
}
public static class RichDocumentImageEdit
{
    public static StyledDocument Insert(StyledDocument source,int blockBoundary,Guid attachmentId,string alt)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(alt);
        if(attachmentId==Guid.Empty||alt.Length is <1 or >256||alt.Trim()!=alt||alt.Any(char.IsControl)||!RichDocumentCodec.IsWellFormedUnicode(alt))throw new InvalidDataException("Invalid inline image identity/alt");
        var nodes=Blocks(source);if(blockBoundary<0||blockBoundary>nodes.Count)throw new ArgumentOutOfRangeException(nameof(blockBoundary));
        nodes.Insert(blockBoundary,JsonSerializer.Serialize(new{type="image",attachmentId=attachmentId.ToString("D"),alt}));return Build(nodes);
    }
    public static StyledDocument Remove(StyledDocument source,int blockIndex)
    {
        ArgumentNullException.ThrowIfNull(source);if(source.SchemaVersion!=2||!RichDocumentCodec.Images(source).Any(image=>image.BlockIndex==blockIndex))throw new InvalidOperationException("Known v2 image at this block required");
        var nodes=Blocks(source);nodes.RemoveAt(blockIndex);return Build(nodes);
    }
    private static List<string> Blocks(StyledDocument source)
    {
        if(!RichDocumentCodec.Inspect(source).Supported)throw new InvalidOperationException("Opaque source is not editable");
        using var parsed=JsonDocument.Parse(source.SourceJson,new(){MaxDepth=16});return parsed.RootElement.GetProperty("nodes").EnumerateArray().Select(node=>node.GetRawText()).ToList();
    }
    private static StyledDocument Build(List<string> nodes)
    {
        var result=new StyledDocument(2,"{\"nodes\":["+string.Join(',',nodes)+"]}");RichDocumentCodec.Inspect(result);return result;
    }
}

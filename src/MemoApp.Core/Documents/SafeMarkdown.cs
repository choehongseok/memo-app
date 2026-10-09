using System.Collections.Immutable;
using System.Globalization;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace MemoApp.Core.Documents;
public sealed record MarkdownSpan(string Text,bool Strong=false,bool Italic=false,bool Code=false);
public sealed record MarkdownBlock(string Kind,int Level,ImmutableArray<MarkdownSpan> Spans)
{public string? ListMarker{get;init;}};
public sealed record MarkdownPreview(bool Complete,string Message,ImmutableArray<MarkdownBlock> Blocks);
public static class SafeMarkdown
{
    public const int MaxSource=65536,MaxNodes=8192,MaxDepth=32,MaxBlocks=1024,MaxOutput=65536;
    private static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder{MaximumNestingDepth=MaxDepth}.DisableHtml().Build();
    private static MarkdownPreview Refused()=>new(false,"미리보기 한도/지원 범위를 넘었습니다. 원문은 그대로 보존합니다.",[]);
    private static bool Preflight(string source)
    {
        if(source.Length>MaxSource||!RichDocumentCodec.IsWellFormedUnicode(source))return false;
        int delimiters=0;var nesting=new Stack<char>();int quotes=0;bool lineStart=true;
        foreach(char value in source)
        {
            if("*[]{}()_<>`".Contains(value)&&++delimiters>MaxNodes)return false;
            if(value is '[' or '(' or '{'){if(nesting.Count>=MaxDepth)return false;nesting.Push(value);}
            else if(value is ']' or ')' or '}')
            {char expected=value==']'?'[':value==')'?'(':'{';if(nesting.TryPeek(out char opening)&&opening==expected)nesting.Pop();}
            if(value is '\r' or '\n'){lineStart=true;quotes=0;}
            else if(lineStart&&value=='>'){if(++quotes>MaxDepth)return false;}
            else if(lineStart&&value is not (' ' or '\t'))lineStart=false;
        }
        return true;
    }
    public static MarkdownPreview Preview(string source)
    {
        ArgumentNullException.ThrowIfNull(source);if(!Preflight(source))return Refused();
        try
        {
            // The fixed pipeline produces an AST only. No HTML renderer, active URL, media or dynamic URL callback.
            var document=Markdown.Parse(source,Pipeline);int scheduled=0,output=0;var blocks=ImmutableArray.CreateBuilder<MarkdownBlock>();
            void Count(){if(++scheduled>MaxNodes)throw new InvalidDataException("Markdown AST budget");}
            void AddText(ImmutableArray<MarkdownSpan>.Builder spans,string text,bool strong=false,bool italic=false,bool code=false)
            {
                if(text.Length>MaxOutput-output||!RichDocumentCodec.IsWellFormedUnicode(text))throw new InvalidDataException("Markdown output budget");output+=text.Length;spans.Add(new(text,strong,italic,code));
            }
            ImmutableArray<MarkdownSpan> Inlines(ContainerInline root,int blockDepth)
            {
                var spans=ImmutableArray.CreateBuilder<MarkdownSpan>();var stack=new Stack<(Inline Node,int Depth,bool Strong,bool Italic)>();
                void PushChildren(ContainerInline owner,int depth,bool strong,bool italic)
                {
                    if(depth>MaxDepth)throw new InvalidDataException("Markdown inline depth");var children=new List<Inline>();for(Inline? child=owner.FirstChild;child is not null;child=child.NextSibling){Count();children.Add(child);}for(int i=children.Count-1;i>=0;i--)stack.Push((children[i],depth,strong,italic));
                }
                PushChildren(root,blockDepth,false,false);
                while(stack.TryPop(out var entry))
                {
                    switch(entry.Node)
                    {
                        case LiteralInline literal:AddText(spans,literal.Content.ToString(),entry.Strong,entry.Italic);break;
                        case CodeInline code:AddText(spans,code.Content,entry.Strong,entry.Italic,true);break;
                        case LineBreakInline line:AddText(spans,line.IsHard?"\n":" ");break;
                        case EmphasisInline emphasis:PushChildren(emphasis,entry.Depth+1,entry.Strong||emphasis.DelimiterCount>=2,entry.Italic||emphasis.DelimiterCount==1);break;
                        case LinkInline link:if(link.IsImage)AddText(spans,"[이미지 비활성] ");PushChildren(link,entry.Depth+1,entry.Strong,entry.Italic);break;
                        case AutolinkInline automatic:AddText(spans,automatic.Url,entry.Strong,entry.Italic);break;
                        case ContainerInline container:PushChildren(container,entry.Depth+1,entry.Strong,entry.Italic);break;
                        default:
                            var span=entry.Node.Span;if(span.Start>=0&&span.End>=span.Start&&span.End<source.Length)AddText(spans,source.Substring(span.Start,span.End-span.Start+1),entry.Strong,entry.Italic);break;
                    }
                }
                return spans.ToImmutable();
            }
            var pending=new Stack<(Block Node,int Depth,string? Marker)>();Count();pending.Push((document,0,null));
            while(pending.TryPop(out var entry))
            {
                if(entry.Depth>MaxDepth)throw new InvalidDataException("Markdown block depth");
                if(entry.Node is ListBlock list)
                {
                    long start=0;if(list.IsOrdered&&!long.TryParse(list.OrderedStart,NumberStyles.None,CultureInfo.InvariantCulture,out start))throw new InvalidDataException("Markdown ordered start");
                    for(int index=list.Count-1;index>=0;index--){Count();string listMarker=list.IsOrdered?(start+index).ToString(CultureInfo.InvariantCulture)+list.OrderedDelimiter+" ":"• ";pending.Push((list[index],entry.Depth+1,listMarker));}continue;
                }
                if(entry.Node is ContainerBlock container)
                {
                    for(int index=container.Count-1;index>=0;index--){Count();pending.Push((container[index],entry.Depth+1,index==0?entry.Marker:null));}continue;
                }
                if(blocks.Count>=MaxBlocks)throw new InvalidDataException("Markdown block count");
                ImmutableArray<MarkdownSpan> spans;string kind="paragraph";int level=0;
                if(entry.Node is CodeBlock codeBlock){var builder=ImmutableArray.CreateBuilder<MarkdownSpan>();AddText(builder,codeBlock.Lines.ToString(),code:true);spans=builder.ToImmutable();kind="code";}
                else if(entry.Node is ThematicBreakBlock){var builder=ImmutableArray.CreateBuilder<MarkdownSpan>();AddText(builder,"────────");spans=builder.ToImmutable();kind="divider";}
                else if(entry.Node is LeafBlock leaf)
                {
                    spans=leaf.Inline is null?[new(leaf.Lines.ToString())]:Inlines(leaf.Inline,entry.Depth);
                    if(leaf.Inline is null){int size=spans.Sum(s=>s.Text.Length);if(size>MaxOutput-output)throw new InvalidDataException("Markdown leaf budget");output+=size;}
                    if(leaf is HeadingBlock heading){kind="heading";level=heading.Level;}
                    else if(entry.Marker is not null)kind="list-item";
                }
                else continue;
                if(entry.Marker is string marker){if(marker.Length>MaxOutput-output)throw new InvalidDataException("Markdown marker budget");output+=marker.Length;}
                blocks.Add(new(kind,level,spans){ListMarker=entry.Marker});
            }
            return new(true,"제한된 안전 미리보기 · 링크/이미지/HTML은 자동 실행하지 않습니다.",blocks.ToImmutable());
        }
        catch(Exception error) when(error is not OutOfMemoryException){return Refused();}
    }
}

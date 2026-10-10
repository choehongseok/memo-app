using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;

namespace MemoApp.Core.Transfer;

// Inert profile values, not source authority or proof of actual native font rendering.
internal sealed record RichParagraphPdfSource(string Title,string Text,string Mode,StyledDocument? OriginalDocument,ImmutableArray<RichPdfParagraph> Paragraphs);
internal sealed record RichPdfParagraph(ImmutableArray<RichPdfRun> Runs);
internal sealed record RichPdfRun(string Text,RichPdfRunStyle Style);
internal sealed record RichPdfRunStyle(bool Bold,bool Underline,bool Strike,string FontFamily,double FontSize,uint Foreground,uint Background);

internal static class RichParagraphPdfCapture
{
    private const int MaxSelection=100,MaxAggregateBytes=16*1024*1024,MaxClusterUnits=128;
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly RichPdfRunStyle Defaults=new(false,false,false,"Segoe UI",14,0xff000000,0);

    internal static ImmutableArray<RichParagraphPdfSource> Capture(IReadOnlyList<NoteDraft> notes,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(notes);token.ThrowIfCancellationRequested();
        int count=notes.Count;if(count is <1 or >MaxSelection)throw Refused();
        var selected=new NoteDraft[count];var ids=new HashSet<Guid>();
        for(int i=0;i<count;i++)
        {
            token.ThrowIfCancellationRequested();var note=notes[i];
            if(note is null||note.IsClosed||note.IsDeleted||!ids.Add(note.Id)||note.Mode is not("plain" or "rich"))throw Refused();
            selected[i]=note;
        }
        int payloadBytes=0,sourceBytes=0;var sources=ImmutableArray.CreateBuilder<RichParagraphPdfSource>(count);
        foreach(var note in selected)
        {
            token.ThrowIfCancellationRequested();
            string title=note.Title,text=note.Text,mode=note.Mode;StyledDocument? original=note.Document;
            if(title.Length>256||text.Length>RichDocumentCodec.MaxText)throw Refused();
            ValidateText(title,token);ValidateText(text,token);
            if(mode=="rich")
            {
                if(original is null||original.SchemaVersion!=1)throw Refused();
                int length=Utf8.GetByteCount(original.SourceJson);
                if(length>RichDocumentCodec.MaxSourceBytes||length>MaxAggregateBytes-sourceBytes)throw Refused();
                sourceBytes=checked(sourceBytes+length);
                var info=RichDocumentCodec.Inspect(original);
                if(!info.Supported||!string.Equals(info.Text,text,StringComparison.Ordinal))throw Refused();
            }
            else if(original is not null)throw Refused();
            // One bounded existing payload at a time; disposed/zeroed even on aggregate refusal.
            using(var payload=TextTransfer.Capture(note))
            {
                if(payload.Bytes.Length>MaxAggregateBytes-payloadBytes)throw Refused();
                payloadBytes=checked(payloadBytes+payload.Bytes.Length);
            }
            var paragraphs=mode=="rich"?ReadParagraphs(original!,token):ImmutableArray<RichPdfParagraph>.Empty;
            token.ThrowIfCancellationRequested();sources.Add(new(title,text,mode,original,paragraphs));
        }
        token.ThrowIfCancellationRequested();return sources.MoveToImmutable();
    }

    private static ImmutableArray<RichPdfParagraph> ReadParagraphs(StyledDocument document,CancellationToken token)
    {
        // Inspect already enforced duplicate fields, token/depth/node/run budgets and scalar types.
        using var parsed=JsonDocument.Parse(document.SourceJson,new(){MaxDepth=16});
        var nodes=parsed.RootElement.GetProperty("nodes");
        var result=ImmutableArray.CreateBuilder<RichPdfParagraph>(nodes.GetArrayLength());
        foreach(var block in nodes.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if(block.GetProperty("type").GetString()!="paragraph")throw Refused();
            var encoded=block.GetProperty("runs");var runs=ImmutableArray.CreateBuilder<RichPdfRun>(encoded.GetArrayLength());
            var combined=new StringBuilder();
            foreach(var run in encoded.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();if(run.TryGetProperty("link",out _))throw Refused();
                string text=run.GetProperty("text").GetString()!;
                string family=run.TryGetProperty("fontFamily",out var font)?font.GetString()!:Defaults.FontFamily;
                if(family is not("Segoe UI" or "Malgun Gothic" or "Segoe UI Symbol" or "Segoe UI Emoji"))throw Refused();
                var style=new RichPdfRunStyle(Flag(run,"bold"),Flag(run,"underline"),Flag(run,"strike"),family,
                    run.TryGetProperty("fontSize",out var size)?size.GetDouble():Defaults.FontSize,
                    Color(run,"foreground",Defaults.Foreground),Color(run,"background",Defaults.Background));
                combined.Append(text);runs.Add(new(text,style));
            }
            string paragraph=combined.ToString();var boundaries=ValidateText(paragraph,token);
            int position=0;RichPdfRunStyle? previous=null;
            foreach(var run in runs)
            {
                token.ThrowIfCancellationRequested();if(run.Text.Length==0)continue;
                // Includes CRLF and combining clusters split between runs. Never pick a style arbitrarily.
                if(previous is not null&&!boundaries[position]&&previous!=run.Style)throw Refused();
                previous=run.Style;position=checked(position+run.Text.Length);
            }
            result.Add(new(runs.MoveToImmutable()));
        }
        return result.MoveToImmutable();
    }

    private static bool Flag(JsonElement run,string name)=>run.TryGetProperty(name,out var value)&&value.GetBoolean();
    private static uint Color(JsonElement run,string name,uint fallback)
    {
        if(!run.TryGetProperty(name,out var value))return fallback;
        string hex=value.GetString()!;uint color=uint.Parse(hex.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return hex.Length==7?color|0xff000000:color;
    }
    private static bool[] ValidateText(string text,CancellationToken token)
    {
        // Same bounded Unicode policy as the approved plain native renderer; coverage/face audit is later native work.
        for(int index=0;index<text.Length;)
        {
            token.ThrowIfCancellationRequested();
            if(Rune.DecodeFromUtf16(text.AsSpan(index),out Rune rune,out int consumed)!=OperationStatus.Done)throw Refused();
            int value=rune.Value;index+=consumed;if(value is '\r' or '\n' or '\t')continue;
            var category=Rune.GetUnicodeCategory(rune);
            bool range=value is >=0x20 and <=0x036f or >=0x0370 and <=0x052f or >=0x1100 and <=0x11ff or >=0x2000 and <=0x2bff or >=0x3000 and <=0x30ff or >=0x3130 and <=0x318f or >=0x3400 and <=0x9fff or >=0xac00 and <=0xd7a3 or >=0xff01 and <=0xff5e or >=0x1d100 and <=0x1d1ff or >=0x1f600 and <=0x1f64f or >=0x20000 and <=0x2fa1f;
            if(!range||category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse||value is 0x20e3 or >=0xfe00 and <=0xfe0f or >=0xe0100 and <=0xe01ef)throw Refused();
        }
        var boundaries=new bool[checked(text.Length+1)];boundaries[0]=boundaries[text.Length]=true;
        var elements=StringInfo.GetTextElementEnumerator(text);int previous=0;
        while(elements.MoveNext())
        {
            token.ThrowIfCancellationRequested();int next=elements.ElementIndex;
            if(next-previous>MaxClusterUnits)throw Refused();boundaries[next]=true;previous=next;
        }
        if(text.Length-previous>MaxClusterUnits)throw Refused();return boundaries;
    }
    private static InvalidDataException Refused()=>new("Selection is outside the bounded rich paragraph PDF source profile");
}

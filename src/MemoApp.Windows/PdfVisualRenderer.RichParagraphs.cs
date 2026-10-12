using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Transfer;

namespace MemoApp.Windows;

internal static partial class PdfVisualRenderer
{
    private const double ParagraphGap=4;
    private static readonly RichPdfRunStyle RichDefaults=new(false,false,false,"Segoe UI",14,0xff000000,0);
    // Actual unsimulated Windows inventory e2bff1c, independently approved in the N02 plan.
    // Symbol/Emoji bold requests resolved to simulated regular faces and are deliberately absent.
    private static readonly (string Family,string Face,bool Bold,string File)[] RichSystemFaces=
    [
        ("Segoe UI","Regular",false,"segoeui.ttf"),("Segoe UI","Bold",true,"segoeuib.ttf"),
        ("Malgun Gothic","Regular",false,"malgun.ttf"),("Malgun Gothic","Bold",true,"malgunbd.ttf"),
        ("Segoe UI Symbol","Regular",false,"seguisym.ttf"),("Segoe UI Emoji","Regular",false,"seguiemj.ttf")
    ];

    internal static async Task<PreparedTextExport> RenderParagraphsAsync(IReadOnlyList<RichParagraphPdfSource> sources,Func<bool> current,CancellationToken token,Action<byte[]>? ownedBuffers=null)
    {
        (Application.Current?.Dispatcher??throw new InvalidOperationException("Display PDF requires the owning WPF application dispatcher")).VerifyAccess();
        ArgumentNullException.ThrowIfNull(sources);ArgumentNullException.ThrowIfNull(current);
        void Current(){token.ThrowIfCancellationRequested();if(!current())throw new OperationCanceledException("Display PDF source authority ended",token);}
        Current();ValidateRichSources(sources,Current);
        if(sources.All(source=>source.Mode=="plain"))
            return await RenderPlainAsync(sources.Select(source=>new VisualPdfSource(source.Title,source.Text)).ToArray(),current,token,ownedBuffers);

        using var builder=new PdfRasterDocumentBuilder(token,ownedBuffers);
        using var formatter=TextFormatter.Create(TextFormattingMode.Ideal);
        var plainProperties=new PlainRunProperties();var plainParagraph=new PlainParagraphProperties(plainProperties);
        DrawingVisual? page=null;DrawingContext? drawing=null;int pages=0,batch=0;double y=Margin;
        async Task Yield(){Current();await Dispatcher.Yield(DispatcherPriority.Background);Current();}
        void BeginPage()
        {
            Current();if(pages>=PdfRasterDocumentBuilder.MaxPages)throw Refused();
            page=new PlainPageVisual();TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);Current();
            drawing=page.RenderOpen();drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,PageWidth,PageHeight));y=Margin;Current();
        }
        async Task EndPage()
        {
            Current();drawing!.Close();drawing=null;Current();
            RenderTargetBitmap? bitmap=null;byte[]? bgra=null,rgb=null;
            try
            {
                bitmap=new RenderTargetBitmap(PageWidth,PageHeight,96,96,PixelFormats.Pbgra32);Current();bitmap.Render(page!);Current();
                bgra=new byte[checked(PageWidth*PageHeight*4)];rgb=new byte[checked(PageWidth*PageHeight*3)];bitmap.CopyPixels(bgra,checked(PageWidth*4),0);Current();
                ownedBuffers?.Invoke(bgra);Current();
                foreach(int corner in new[]{0,(PageWidth-1)*4,(PageHeight-1)*PageWidth*4,bgra.Length-4})
                    if(bgra[corner]!=255||bgra[corner+1]!=255||bgra[corner+2]!=255||bgra[corner+3]!=255)throw new InvalidDataException("Display PDF native white page background is missing");
                for(int row=0;row<PageHeight;row++)
                {
                    Current();int from=checked(row*PageWidth*4),to=checked(row*PageWidth*3);
                    for(int column=0;column<PageWidth;column++)CompositeWhiteRgb(bgra.AsSpan(from+column*4,4),rgb.AsSpan(to+column*3,3));
                }
                ownedBuffers?.Invoke(rgb);Current();builder.AddRgbPage(PageWidth,PageHeight,rgb);Current();pages++;
            }
            finally
            {
                if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);
                try{bitmap?.Clear();}finally{page=null;}
            }
            await Yield();
        }
        async Task RenderSection(string text,TextSource source,TextParagraphProperties properties,RichLayout? rich,double gap)
        {
            bool[] boundaries=GraphemeBoundaries(text);int index=0;TextLineBreak? previous=null;
            try
            {
                while(index<=text.Length)
                {
                    Current();
                    using(var line=formatter.FormatLine(source,index,DipWidth-2*Margin,properties,previous))
                    {
                        Current();int next=checked(index+line.Length),end=Math.Min(next,text.Length);
                        if(line.Length<=0||next>text.Length+1||!boundaries[index]||!boundaries[end])throw new InvalidDataException("Display PDF line split an original grapheme or failed to advance");
                        Rect ink;double above,height;
                        if(rich is null)
                        {
                            // The original plain layout, including its regular-face and geometry policy.
                            ink=AuditLine(line,text,index,end);
                            if(!Finite(line.Height)||line.Height<=0||!Finite(line.WidthIncludingTrailingWhitespace)||line.WidthIncludingTrailingWhitespace>DipWidth-2*Margin+0.01)throw Refused();
                            above=ink.IsEmpty?0:Math.Min(0,ink.Top);double below=ink.IsEmpty?line.Height:Math.Max(line.Height,ink.Bottom);height=below-above;
                            if(!Finite(height)||height>DipHeight-2*Margin||!ink.IsEmpty&&(Margin+ink.Left<0||Margin+ink.Right>DipWidth))throw Refused();
                        }
                        else
                        {
                            ink=AuditLineCore(line,text,index,end,(glyph,start,length)=>AuditRichGlyph(glyph,rich,start,length));
                            var complete=new DrawingGroup();
                            try
                            {
                                Current();using(var context=complete.Open())line.Draw(context,new Point(0,0),InvertAxes.None);Current();
                                ink.Union(complete.Bounds);(above,height)=RichLineGeometry(line.Height,line.WidthIncludingTrailingWhitespace,ink);
                            }
                            finally{complete.Children.Clear();}
                        }
                        double finalGap=next>text.Length?gap:0;
                        if(height+finalGap>DipHeight-2*Margin)throw Refused();
                        if(y+height+finalGap>DipHeight-Margin){await EndPage();BeginPage();}
                        Current();line.Draw(drawing!,new Point(Margin,y-above),InvertAxes.None);Current();y+=height+finalGap;
                        previous?.Dispose();previous=line.GetTextLineBreak();index=next;batch++;
                    }
                    if(batch>=LineBatch){batch=0;await Yield();}
                }
            }
            finally{previous?.Dispose();}
        }
        try
        {
            foreach(var source in sources)
            {
                Current();BeginPage();
                if(source.Mode=="plain")
                {
                    string text=Layout(new(source.Title,source.Text));await RenderSection(text,new PlainTextSource(text,plainProperties),plainParagraph,null,0);
                }
                else
                {
                    if(source.Title.Length>0)
                    {
                        // Title and one separating blank line retain the original plain title style/layout.
                        string title=Layout(new("",source.Title))+"\n";await RenderSection(title,new PlainTextSource(title,plainProperties),plainParagraph,null,0);
                    }
                    foreach(var paragraph in source.Paragraphs)
                    {
                        Current();RichLayout layout=MapRichParagraph(paragraph,Current);var properties=new Dictionary<RichPdfRunStyle,RichRunProperties>();
                        // Empty source runs still request a font and are part of the closed supported profile.
                        foreach(var run in paragraph.Runs)
                        {Current();if(!properties.ContainsKey(run.Style))properties.Add(run.Style,new RichRunProperties(run.Style));Current();}
                        if(!properties.TryGetValue(RichDefaults,out var defaults)){defaults=new RichRunProperties(RichDefaults);properties.Add(RichDefaults,defaults);}
                        Current();var textSource=new RichTextSource(layout,properties,defaults);
                        await RenderSection(layout.Text,textSource,new RichParagraphProperties(defaults),layout,ParagraphGap);
                    }
                }
                await EndPage();
            }
            Current();var prepared=builder.Finish();
            try{Current();ownedBuffers?.Invoke(prepared.Bytes);Current();return prepared;}
            catch{prepared.Dispose();throw;}
        }
        finally{try{drawing?.Close();}finally{page=null;}}
    }

    private static void ValidateRichSources(IReadOnlyList<RichParagraphPdfSource> sources,Action current)
    {
        if(sources.Count is <1 or >100)throw Refused();int payloadBytes=0,sourceBytes=0,minimumPages=0;
        foreach(var source in sources)
        {
            current();if(source is null||source.Title is null||source.Text is null||source.Title.Length>256||source.Text.Length>65536||source.Paragraphs.IsDefault)throw Refused();
            ValidateUnicode(source.Title);ValidateUnicode(source.Text);
            static string Newlines(string text)=>text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            string payload=(source.Title.Length==0?"":Newlines(source.Title)+"\n\n")+Newlines(source.Text);
            payloadBytes=checked(payloadBytes+StrictUtf8.GetByteCount(payload.Replace("\n","\r\n",StringComparison.Ordinal)));if(payloadBytes>MaxInputBytes)throw Refused();
            int explicitLines=1;double minimumHeight;
            if(source.Mode=="plain")
            {
                if(source.OriginalDocument is not null||source.Paragraphs.Length!=0)throw Refused();
                explicitLines+=Layout(new(source.Title,source.Text)).Count(value=>value=='\n');
                int linesPerPage=(int)Math.Floor((DipHeight-2*Margin)/LineHeight);minimumPages=checked(minimumPages+(explicitLines+linesPerPage-1)/linesPerPage);
            }
            else if(source.Mode=="rich")
            {
                if(source.OriginalDocument is not {SchemaVersion:1} original||source.Paragraphs.Length>1024)throw Refused();
                var inspected=RichDocumentCodec.Inspect(original);if(!inspected.Supported||!string.Equals(inspected.Text,source.Text,StringComparison.Ordinal))throw Refused();
                sourceBytes=checked(sourceBytes+StrictUtf8.GetByteCount(original.SourceJson));if(sourceBytes>MaxInputBytes)throw Refused();
                ValidateRichOriginalBinding(source,original,current);
                var projection=new StringBuilder();int runs=0;minimumHeight=0;
                foreach(var paragraph in source.Paragraphs)
                {
                    current();if(paragraph is null||paragraph.Runs.IsDefault)throw Refused();runs=checked(runs+paragraph.Runs.Length);if(runs>4096)throw Refused();
                    RichLayout mapped=MapRichParagraph(paragraph,current);if(projection.Length>0||minimumHeight>0)projection.Append('\n');projection.Append(mapped.Original);
                    minimumHeight=checked(minimumHeight+(1+mapped.Text.Count(value=>value=='\n'))*LineHeight+ParagraphGap);
                }
                if(!string.Equals(projection.ToString(),source.Text,StringComparison.Ordinal))throw Refused();
                if(source.Title.Length>0)minimumHeight+=(2+Newlines(source.Title).Count(value=>value=='\n'))*LineHeight;
                minimumPages=checked(minimumPages+Math.Max(1,(int)Math.Ceiling(minimumHeight/(DipHeight-2*Margin))));
            }
            else throw Refused();
            if(minimumPages>PdfRasterDocumentBuilder.MaxPages)throw Refused();current();
        }
    }
    private static void ValidateRichOriginalBinding(RichParagraphPdfSource source,StyledDocument original,Action current)
    {
        // The bounded codec inspection above validates scalar types, fields, counts and Unicode.
        // Equal projections alone do not bind the detached DTO's structure or styles to that source.
        using var parsed=JsonDocument.Parse(original.SourceJson,new JsonDocumentOptions{MaxDepth=16});
        var nodes=parsed.RootElement.GetProperty("nodes");if(nodes.GetArrayLength()!=source.Paragraphs.Length)throw Refused();int nodeIndex=0;
        foreach(var node in nodes.EnumerateArray())
        {
            current();if(node.GetProperty("type").GetString()!="paragraph")throw Refused();
            var paragraph=source.Paragraphs[nodeIndex++];if(paragraph is null||paragraph.Runs.IsDefault)throw Refused();
            var encoded=node.GetProperty("runs");if(encoded.GetArrayLength()!=paragraph.Runs.Length)throw Refused();int runIndex=0;
            foreach(var raw in encoded.EnumerateArray())
            {
                current();if(raw.TryGetProperty("link",out _))throw Refused();
                var run=paragraph.Runs[runIndex++];
                if(run is null||run.Text is null||run.Style is null||!string.Equals(run.Text,raw.GetProperty("text").GetString(),StringComparison.Ordinal))throw Refused();
                bool Flag(string name)=>raw.TryGetProperty(name,out var field)&&field.GetBoolean();
                uint ColorValue(string name,uint fallback)
                {
                    if(!raw.TryGetProperty(name,out var field))return fallback;
                    string hex=field.GetString()!;uint value=uint.Parse(hex.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
                    return hex.Length==7?value|0xff000000:value;
                }
                var expected=new RichPdfRunStyle(Flag("bold"),Flag("underline"),Flag("strike"),
                    raw.TryGetProperty("fontFamily",out var family)?family.GetString()!:RichDefaults.FontFamily,
                    raw.TryGetProperty("fontSize",out var size)?size.GetDouble():RichDefaults.FontSize,
                    ColorValue("foreground",RichDefaults.Foreground),ColorValue("background",RichDefaults.Background));
                if(run.Style!=expected)throw Refused();
                // An empty run has no glyph to audit. Unsupported requested bold must reject the
                // complete selection here, before even an earlier valid note starts native work.
                if(run.Text.Length==0&&!RichSystemFaces.Any(face=>face.Family==expected.FontFamily&&face.Bold==expected.Bold))throw Refused();
            }
        }
    }
    private sealed record RichSpan(int Start,int Length,RichPdfRunStyle Style);
    private sealed record RichLayout(string Original,string Text,RichSpan[] Spans,int[] SourceOffsets)
    {
        internal RichSpan SpanAt(int index)
        {
            int left=0,right=Spans.Length-1;
            while(left<=right){int middle=left+(right-left)/2;var span=Spans[middle];if(index<span.Start)right=middle-1;else if(index>=span.Start+span.Length)left=middle+1;else return span;}
            throw Refused();
        }
        internal RichPdfRunStyle StyleAt(int index)=>SpanAt(index).Style;
    }
    private static RichLayout MapRichParagraph(RichPdfParagraph paragraph,Action current)
    {
        var combined=new StringBuilder();var raw=new List<RichSpan>();
        foreach(var run in paragraph.Runs)
        {
            current();if(run is null||run.Text is null||run.Style is null||!Finite(run.Style.FontSize)||run.Style.FontSize is <8 or >96||!SystemFaces.ContainsKey(run.Style.FontFamily))throw Refused();
            if(run.Text.Length>65536-combined.Length)throw Refused();
            if(run.Text.Length>0){raw.Add(new(combined.Length,run.Text.Length,run.Style));combined.Append(run.Text);}
        }
        string original=combined.ToString();ValidateUnicode(original);bool[] boundaries=GraphemeBoundaries(original);
        for(int i=1;i<raw.Count;i++)if(!boundaries[raw[i].Start]&&raw[i-1].Style!=raw[i].Style)throw new InvalidDataException("Conflicting PDF styles within an original grapheme");
        var normalized=new StringBuilder();var offsets=new List<int>();var spans=new List<RichSpan>();int runIndex=0;
        for(int index=0;index<original.Length;index++)
        {
            if(index%256==0)current();while(index>=raw[runIndex].Start+raw[runIndex].Length)runIndex++;var style=raw[runIndex].Style;char value=original[index];int sourceOffset=index;
            if(value=='\r'){if(index+1<original.Length&&original[index+1]=='\n')index++;value='\n';}
            int count=value=='\t'?4:1;int start=normalized.Length;
            for(int repeat=0;repeat<count;repeat++){normalized.Append(value=='\t'?' ':value);offsets.Add(sourceOffset);}
            if(spans.Count>0&&spans[^1].Style==style){var last=spans[^1];spans[^1]=last with{Length=checked(last.Length+count)};}
            else spans.Add(new(start,count,style));
        }
        return new(original,normalized.ToString(),spans.ToArray(),offsets.ToArray());
    }
    private static void AuditRichGlyph(GlyphRun glyph,RichLayout layout,int start,int length)
    {
        RichPdfRunStyle? previous=null;
        for(int index=start;index<start+length;index++)
        {
            var style=layout.StyleAt(index);
            if(style!=previous)
            {
                // An indexed native glyph run may cover adjacent paint-only styles. Audit each effective
                // span's actual weight/size instead of assuming the native segmentation equals source runs.
                ValidateRichResolvedFont(glyph.GlyphTypeface,style.Bold);
                if(!Finite(glyph.FontRenderingEmSize)||Math.Abs(glyph.FontRenderingEmSize-style.FontSize)>0.000001)throw new InvalidDataException("Native PDF fallback changed the requested font size");
                previous=style;
            }
            int original=layout.SourceOffsets[index];char from=layout.Original[original],to=layout.Text[index];
            if(from!=to&&!(from=='\t'&&to==' ')&&!(from=='\r'&&to=='\n'))throw new InvalidDataException("Native PDF layout changed its original source mapping");
        }
    }
    internal static void ValidateRichResolvedFont(GlyphTypeface face,bool bold)
    {
        ArgumentNullException.ThrowIfNull(face);ValidateFontOrigin(face.FontUri);var english=CultureInfo.GetCultureInfo("en-US");
        if(!face.FamilyNames.TryGetValue(english,out string? family)||!face.FaceNames.TryGetValue(english,out string? name)
            ||face.Weight!=(bold?FontWeights.Bold:FontWeights.Normal)||face.Style!=FontStyles.Normal||face.Stretch!=FontStretches.Normal||face.StyleSimulations!=StyleSimulations.None||face.FontUri.Fragment.Length!=0)
            throw new InvalidDataException("Rich PDF actual face does not preserve the requested unsimulated style");
        string fonts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Fonts");
        if(!RichSystemFaces.Any(approved=>approved.Family==family&&approved.Face==name&&approved.Bold==bold
            &&Path.GetFullPath(face.FontUri.LocalPath).Equals(Path.GetFullPath(Path.Combine(fonts,approved.File)),StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Rich PDF actual face or Windows Fonts origin is outside the six approved faces");
    }
    private static (double Above,double Height) RichLineGeometry(double lineHeight,double width,Rect ink)
    {
        if(!Finite(lineHeight)||lineHeight<=0||!Finite(width)||width<0||width>DipWidth-2*Margin+0.01)throw Refused();
        if(!ink.IsEmpty&&(!Finite(ink.Left)||!Finite(ink.Top)||!Finite(ink.Right)||!Finite(ink.Bottom)||Margin+ink.Left<0||Margin+ink.Right>DipWidth))throw Refused();
        double above=ink.IsEmpty?0:Math.Min(0,ink.Top),below=ink.IsEmpty?lineHeight:Math.Max(lineHeight,ink.Bottom);
        double height=Math.Max(LineHeight,below-above);if(!Finite(height)||height>DipHeight-2*Margin)throw Refused();return(above,height);
    }
    private sealed class RichRunProperties:TextRunProperties
    {
        internal RichRunProperties(RichPdfRunStyle style)
        {
            Typeface=new(new FontFamily(style.FontFamily),FontStyles.Normal,style.Bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal);
            if(!Typeface.TryGetGlyphTypeface(out var face))throw new InvalidDataException("Rich PDF requested font has no approved native face");
            ValidateRichResolvedFont(face,style.Bold);
            if(!face.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-US"),out var family)||family!=style.FontFamily)throw new InvalidDataException("Rich PDF requested family was substituted");
            FontRenderingEmSize=style.FontSize;ForegroundBrush=Solid(style.Foreground);
            // A zero-alpha run background has no ink. Emitting its rectangle would let
            // fallback-font ascent bounds move otherwise unchanged visible glyphs.
            BackgroundBrush=(style.Background>>24)==0?null:Solid(style.Background);
            // WPF TextBlock supplies Run decorations through both its TextSpanModifier and
            // TextProperties. Match that effective collection so translucent decoration
            // compositing agrees with the read-only viewer and independent native Run oracle.
            var decorations=new TextDecorationCollection();
            for(int inlineSource=0;inlineSource<2;inlineSource++)
            {if(style.Underline)decorations.Add(System.Windows.TextDecorations.Underline[0]);if(style.Strike)decorations.Add(System.Windows.TextDecorations.Strikethrough[0]);}
            decorations.Freeze();TextDecorations=decorations;
        }
        private static SolidColorBrush Solid(uint value){var brush=new SolidColorBrush(Color.FromArgb((byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value));brush.Freeze();return brush;}
        public override Typeface Typeface{get;}public override double FontRenderingEmSize{get;}public override double FontHintingEmSize=>FontRenderingEmSize;
        public override TextDecorationCollection TextDecorations{get;}public override Brush ForegroundBrush{get;}public override Brush? BackgroundBrush{get;}
        public override CultureInfo CultureInfo=>CultureInfo.GetCultureInfo("ko-KR");public override TextEffectCollection TextEffects=>new();
    }
    private sealed class RichParagraphProperties(RichRunProperties defaults):TextParagraphProperties
    {
        public override FlowDirection FlowDirection=>FlowDirection.LeftToRight;public override TextAlignment TextAlignment=>TextAlignment.Left;
        public override double LineHeight=>0;public override bool FirstLineInParagraph=>true;public override TextRunProperties DefaultTextRunProperties=>defaults;
        public override TextWrapping TextWrapping=>TextWrapping.Wrap;public override TextMarkerProperties? TextMarkerProperties=>null;public override double Indent=>0;
    }
    private sealed class RichTextSource(RichLayout layout,IReadOnlyDictionary<RichPdfRunStyle,RichRunProperties> properties,RichRunProperties defaults):TextSource
    {
        public override TextRun GetTextRun(int index)
        {
            if(index==layout.Text.Length)return new TextEndOfParagraph(1);if(index<0||index>layout.Text.Length)throw Refused();
            if(layout.Text[index]=='\n')return new TextEndOfLine(1,properties[layout.StyleAt(index)]);
            var span=layout.SpanAt(index);int end=span.Start+span.Length,newline=layout.Text.IndexOf('\n',index);if(newline>=0)end=Math.Min(end,newline);
            return new TextCharacters(layout.Text,index,end-index,properties[span.Style]);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit)=>new(limit,new(defaults.CultureInfo,new CharacterBufferRange(layout.Text,0,limit)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index)=>index;
    }
}

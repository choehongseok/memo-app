using System.Buffers;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Transfer;

namespace MemoApp.Windows;

internal readonly record struct VisualPdfSource(string Title,string Body);

// Detached plain sources only. No editor visual, arbitrary markup, image URI or font-file input.
internal static class PdfVisualRenderer
{
    internal const int PageWidth=794,PageHeight=1123;
    internal const double Margin=40*96.0/72,FontSize=11*96.0/72,LineHeight=24;
    private const double DipWidth=210*96.0/25.4,DipHeight=297*96.0/25.4;
    private const int MaxInputBytes=16*1024*1024,LineBatch=32,MaxClusterUnits=128;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    private static readonly IReadOnlyDictionary<string,string> SystemFaces=new Dictionary<string,string>(StringComparer.Ordinal)
    {{"Segoe UI","segoeui.ttf"},{"Malgun Gothic","malgun.ttf"},{"Segoe UI Symbol","seguisym.ttf"},{"Segoe UI Emoji","seguiemj.ttf"}};

    internal static IReadOnlyList<VisualPdfSource> CapturePlainSources(IEnumerable<NoteDraft> selection,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(selection);var notes=selection.Take(101).ToArray();
        if(notes.Length is <1 or >100||notes.Select(n=>n.Id).Distinct().Count()!=notes.Length)throw Refused();
        var sources=new VisualPdfSource[notes.Length];int total=0;
        for(int i=0;i<notes.Length;i++)
        {
            token.ThrowIfCancellationRequested();var note=notes[i];
            if(note.Mode!="plain")throw new InvalidDataException("Display PDF supports plain notes only; rich and Markdown are refused");
            using var validated=TextTransfer.Capture(note);total=checked(total+validated.Bytes.Length);if(total>MaxInputBytes)throw Refused();
            sources[i]=new(note.Title,note.Text);
        }
        return sources;
    }

    internal static async Task<PreparedTextExport> RenderPlainAsync(IReadOnlyList<VisualPdfSource> sources,Func<bool> current,CancellationToken token,Action<byte[]>? ownedBuffers=null)
    {
        (Application.Current?.Dispatcher??throw new InvalidOperationException("Display PDF requires the owning WPF application dispatcher")).VerifyAccess();ArgumentNullException.ThrowIfNull(sources);ArgumentNullException.ThrowIfNull(current);
        if(sources.Count is <1 or >100)throw Refused();
        void Current(){token.ThrowIfCancellationRequested();if(!current())throw new OperationCanceledException("Display PDF source authority ended",token);}
        Current();int inputBytes=0,minimumPages=0;
        foreach(var source in sources)
        {
            Current();if(source.Title is null||source.Body is null||source.Title.Length>256||source.Body.Length>65536)throw Refused();
            ValidateUnicode(source.Title);ValidateUnicode(source.Body);
            inputBytes=checked(inputBytes+StrictUtf8.GetByteCount(source.Title)+StrictUtf8.GetByteCount(source.Body));if(inputBytes>MaxInputBytes)throw Refused();
            string layout=Layout(source);int explicitLines=1;foreach(char value in layout){if(value=='\n')explicitLines++;}
            int linesPerPage=checked((int)Math.Floor((DipHeight-2*Margin)/LineHeight));minimumPages=checked(minimumPages+(explicitLines+linesPerPage-1)/linesPerPage);if(minimumPages>PdfRasterDocumentBuilder.MaxPages)throw Refused();
        }
        using var builder=new PdfRasterDocumentBuilder(token);using var formatter=TextFormatter.Create(TextFormattingMode.Ideal);
        var runProperties=new PlainRunProperties();var paragraph=new PlainParagraphProperties(runProperties);
        DrawingVisual? page=null;DrawingContext? drawing=null;int pages=0,linesInBatch=0;double y=Margin;
        void BeginPage()
        {
            Current();if(pages>=PdfRasterDocumentBuilder.MaxPages)throw Refused();
            page=new DrawingVisual();TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);
            drawing=page.RenderOpen();drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,PageWidth,PageHeight));y=Margin;
        }
        async Task Yield()
        {Current();await Dispatcher.Yield(DispatcherPriority.Background);Current();}
        async Task EndPage()
        {
            Current();drawing!.Close();drawing=null;
            var bitmap=new RenderTargetBitmap(PageWidth,PageHeight,96,96,PixelFormats.Pbgra32);
            byte[]? bgra=null,rgb=null;
            try
            {
                Current();bitmap.Render(page!);Current();
                bgra=new byte[checked(PageWidth*PageHeight*4)];rgb=new byte[checked(PageWidth*PageHeight*3)];bitmap.CopyPixels(bgra,checked(PageWidth*4),0);
                ownedBuffers?.Invoke(bgra); // Optional ownership observer; production has no observer.
                for(int row=0;row<PageHeight;row++)
                {
                    Current();int start=checked(row*PageWidth*4),outStart=checked(row*PageWidth*3);
                    for(int column=0;column<PageWidth;column++)
                    {
                        int pixel=start+column*4,output=outStart+column*3;
                        if(bgra[pixel+3]!=255)throw new InvalidDataException("Display PDF page must be opaque white-backed RGB");
                        rgb[output]=bgra[pixel+2];rgb[output+1]=bgra[pixel+1];rgb[output+2]=bgra[pixel];
                    }
                }
                ownedBuffers?.Invoke(rgb);Current();builder.AddRgbPage(PageWidth,PageHeight,rgb);pages++;
            }
            finally
            {
                if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);
                bgra=rgb=null;bitmap.Clear();page=null; // Native/GC copies have no secure-erasure or immediate-release guarantee.
            }
            await Yield();
        }
        try
        {
            foreach(var source in sources)
            {
                Current();string text=Layout(source);bool[] boundaries=GraphemeBoundaries(text);var textSource=new PlainTextSource(text,runProperties);int index=0;TextLineBreak? previous=null;BeginPage();
                try
                {
                    while(index<=text.Length)
                    {
                        Current();
                        using(var line=formatter.FormatLine(textSource,index,DipWidth-2*Margin,paragraph,previous))
                        {
                            Current();int next=checked(index+line.Length),end=Math.Min(next,text.Length);
                            if(line.Length<=0||next>text.Length+1||!boundaries[index]||!boundaries[end])throw new InvalidDataException("Display PDF line split an original grapheme or failed to advance");
                            Rect ink=AuditLine(line,text,index,end);
                            if(!Finite(line.Height)||line.Height<=0||!Finite(line.WidthIncludingTrailingWhitespace)||line.WidthIncludingTrailingWhitespace>DipWidth-2*Margin+0.01)throw Refused();
                            double above=ink.IsEmpty?0:Math.Min(0,ink.Top),below=ink.IsEmpty?line.Height:Math.Max(line.Height,ink.Bottom),height=below-above;
                            if(!Finite(height)||height>DipHeight-2*Margin)throw new InvalidDataException("Display PDF line ink exceeds a page");
                            if(!ink.IsEmpty&&(Margin+ink.Left<0||Margin+ink.Right>DipWidth))throw new InvalidDataException("Display PDF line ink exceeds page width");
                            if(y+height>DipHeight-Margin){await EndPage();BeginPage();}
                            Current();line.Draw(drawing!,new Point(Margin,y-above),InvertAxes.None);Current();y+=height;
                            previous?.Dispose();previous=line.GetTextLineBreak();index=next;linesInBatch++;
                        }
                        // Only this page's inert drawing commands survive the bounded batch yield.
                        if(linesInBatch>=LineBatch){linesInBatch=0;await Yield();}
                    }
                }
                finally{previous?.Dispose();}
                await EndPage();
            }
            Current();var prepared=builder.Finish();try{Current();return prepared;}catch{prepared.Dispose();throw;}
        }
        finally{drawing?.Close();page=null;}
    }

    private static string Layout(VisualPdfSource source)
    {
        string text=(source.Title.Length==0?"":source.Title+"\n\n")+source.Body;
        return text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\t","    ",StringComparison.Ordinal);
    }
    private static void ValidateUnicode(string text)
    {
        for(int index=0;index<text.Length;)
        {
            if(Rune.DecodeFromUtf16(text.AsSpan(index),out Rune rune,out int consumed)!=OperationStatus.Done)throw new InvalidDataException("Malformed display PDF Unicode");
            int value=rune.Value;index+=consumed;if(value is '\r' or '\n' or '\t')continue;
            var category=Rune.GetUnicodeCategory(rune);
            bool range=value is >=0x20 and <=0x036f or >=0x0370 and <=0x052f or >=0x1100 and <=0x11ff or >=0x2000 and <=0x2bff or >=0x3000 and <=0x30ff or >=0x3130 and <=0x318f or >=0x3400 and <=0x9fff or >=0xac00 and <=0xd7a3 or >=0xff01 and <=0xff5e or >=0x1d100 and <=0x1d1ff or >=0x1f600 and <=0x1f64f or >=0x20000 and <=0x2fa1f;
            if(!range||category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse||value is 0x20e3 or >=0xfe00 and <=0xfe0f or >=0xe0100 and <=0xe01ef)throw new InvalidDataException("Display PDF Unicode sequence is outside verified shaping policy");
        }
        _=GraphemeBoundaries(text);
    }
    private static bool[] GraphemeBoundaries(string text)
    {
        var boundaries=new bool[checked(text.Length+1)];boundaries[0]=boundaries[text.Length]=true;var elements=StringInfo.GetTextElementEnumerator(text);int previous=0;
        while(elements.MoveNext())
        {
            int index=elements.ElementIndex;if(index-previous>MaxClusterUnits)throw new InvalidDataException("Display PDF grapheme length limit");boundaries[index]=true;previous=index;
        }
        if(text.Length-previous>MaxClusterUnits)throw new InvalidDataException("Display PDF grapheme length limit");return boundaries;
    }

    private static Rect AuditLine(TextLine line,string text,int start,int end)
    {
        var coverage=new bool[end-start];Rect ink=Rect.Empty;
        foreach(var indexed in line.GetIndexedGlyphRuns())
        {
            var glyph=indexed.GlyphRun;int runStart=indexed.TextSourceCharacterIndex,length=indexed.TextSourceLength;
            if(length<=0||runStart<start||runStart>end-length||glyph.BidiLevel!=0||glyph.IsSideways||glyph.GlyphIndices.Count==0||glyph.GlyphIndices.Any(index=>index==0))throw new InvalidDataException("Missing or unsupported native PDF glyph coverage");
            ValidateResolvedFont(glyph.GlyphTypeface);
            if(glyph.Characters is null||glyph.Characters.Count!=length)throw new InvalidDataException("Native PDF glyph lacks original character mapping");
            for(int i=0;i<length;i++)
            {
                if(glyph.Characters[i]!=text[runStart+i]||coverage[runStart-start+i])throw new InvalidDataException("Native PDF glyph source mapping changed or overlapped");coverage[runStart-start+i]=true;
            }
            if((runStart>0&&char.IsLowSurrogate(text[runStart]))||(runStart+length<text.Length&&char.IsLowSurrogate(text[runStart+length])))throw new InvalidDataException("Native PDF run split a supplementary scalar");
            var clusters=glyph.ClusterMap;
            if(clusters is null||clusters.Count==0)
            {if(glyph.GlyphIndices.Count!=length||glyph.Characters.Any(char.IsSurrogate))throw new InvalidDataException("Native PDF glyph needs an explicit cluster map");}
            else
            {
                if(clusters.Count!=length)throw new InvalidDataException("Native PDF cluster coverage mismatch");
                for(int i=0;i<length;i++)
                {
                    if(clusters[i]>=glyph.GlyphIndices.Count||(i>0&&clusters[i]<clusters[i-1])||(char.IsLowSurrogate(glyph.Characters[i])&&(i==0||clusters[i]!=clusters[i-1])))throw new InvalidDataException("Native PDF cluster split or invalid mapping");
                }
            }
            Rect bounds=glyph.ComputeInkBoundingBox();if(!bounds.IsEmpty){bounds.Offset(glyph.BaselineOrigin.X,glyph.BaselineOrigin.Y);if(!Finite(bounds.Left)||!Finite(bounds.Top)||!Finite(bounds.Right)||!Finite(bounds.Bottom))throw Refused();ink.Union(bounds);}
        }
        for(int i=0;i<coverage.Length;i++)if(!coverage[i]&&text[start+i] is not (' ' or '\n'))throw new InvalidDataException("Native PDF formatter omitted original visible text");
        return ink;
    }
    internal static void ValidateFontOrigin(Uri origin)
    {
        if(!origin.IsAbsoluteUri||!origin.IsFile||origin.IsUnc||origin.Query.Length!=0)throw new InvalidDataException("Display PDF font must be a local Windows system font");
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);if(string.IsNullOrEmpty(windows))throw Refused();
        string fonts=Path.GetFullPath(Path.Combine(windows,"Fonts"))+Path.DirectorySeparatorChar,path=LocalFilePath.Resolve(origin.LocalPath);
        if(!path.StartsWith(fonts,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Display PDF font origin is outside Windows system Fonts");
        LocalFilePath.CheckAncestors(path,true);
    }
    internal static void ValidateResolvedFont(GlyphTypeface face)
    {
        ArgumentNullException.ThrowIfNull(face);ValidateFontOrigin(face.FontUri);
        var english=CultureInfo.GetCultureInfo("en-US");
        if(!face.FamilyNames.TryGetValue(english,out string? family)||!SystemFaces.TryGetValue(family,out string? file)
            ||!Path.GetFileName(face.FontUri.LocalPath).Equals(file,StringComparison.OrdinalIgnoreCase)
            ||!face.FaceNames.TryGetValue(english,out string? name)||name is not ("Regular" or "Normal")
            ||face.Style!=FontStyles.Normal||face.Weight!=FontWeights.Normal||face.Stretch!=FontStretches.Normal||face.StyleSimulations!=StyleSimulations.None)
            throw new InvalidDataException("Display PDF resolved font family, regular face or known system filename is not approved");
    }
    private static bool Finite(double value)=>double.IsFinite(value);
    private static InvalidDataException Refused()=>new("Display PDF source, page or geometry limit");

    private sealed class PlainRunProperties:TextRunProperties
    {
        public override Typeface Typeface{get;}=new(new FontFamily("Global User Interface"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
        public override double FontRenderingEmSize=>FontSize;public override double FontHintingEmSize=>FontSize;
        public override TextDecorationCollection TextDecorations=>new();public override Brush ForegroundBrush=>Brushes.Black;public override Brush BackgroundBrush=>Brushes.Transparent;
        public override CultureInfo CultureInfo=>CultureInfo.GetCultureInfo("ko-KR");public override TextEffectCollection TextEffects=>new();
    }
    private sealed class PlainParagraphProperties(PlainRunProperties properties):TextParagraphProperties
    {
        public override FlowDirection FlowDirection=>FlowDirection.LeftToRight;public override TextAlignment TextAlignment=>TextAlignment.Left;public override double LineHeight=>PdfVisualRenderer.LineHeight;
        public override bool FirstLineInParagraph=>true;public override TextRunProperties DefaultTextRunProperties=>properties;public override TextWrapping TextWrapping=>TextWrapping.Wrap;
        public override TextMarkerProperties? TextMarkerProperties=>null;public override double Indent=>0;
    }
    private sealed class PlainTextSource(string text,PlainRunProperties properties):TextSource
    {
        public override TextRun GetTextRun(int index)
        {
            if(index==text.Length)return new TextEndOfParagraph(1);if(index<0||index>text.Length)throw Refused();
            if(text[index]=='\n')return new TextEndOfLine(1);int newline=text.IndexOf('\n',index);return new TextCharacters(text,index,(newline<0?text.Length:newline)-index,properties);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit)=>new(limit,new(properties.CultureInfo,new CharacterBufferRange(text,0,limit)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index)=>index;
    }
}

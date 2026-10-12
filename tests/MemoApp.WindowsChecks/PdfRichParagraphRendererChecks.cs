using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;

internal static partial class Program
{
    private const double RichReferenceMargin=40*96.0/72,RichReferenceWidth=210*96.0/25.4-2*RichReferenceMargin;
    private const string RichBasicJson="{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"합성 \"},{\"text\":\"굵은 한글 ABC\",\"bold\":true,\"underline\":true,\"strike\":true,\"fontFamily\":\"Malgun Gothic\",\"foreground\":\"#CC0000\",\"background\":\"#80FFFF00\"},{\"text\":\" é 😀\"}]}]}";
    private sealed record RichRasterFixture(string Name,StyledDocument Document,Func<TextBlock> Reference);

    // This method deliberately finds the API at runtime so the unchanged baseline DLL is a meaningful RED.
    private static Task<PreparedTextExport> RichRender(IReadOnlyList<RichParagraphPdfSource> sources,Func<bool>? current=null,CancellationToken token=default,Action<byte[]>? owned=null)
    {
        var method=typeof(PdfVisualRenderer).GetMethod("RenderParagraphsAsync",BindingFlags.Static|BindingFlags.NonPublic)
            ??throw new Exception("N02 rich native renderer API missing: RenderParagraphsAsync");
        try{return (Task<PreparedTextExport>)method.Invoke(null,[sources,current??(()=>true),token,owned])!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static RichParagraphPdfSource RichSource(StyledDocument document,string title="")
    {
        string text=RichDocumentCodec.Inspect(document).Text!;var now=DateTimeOffset.UtcNow;
        var stored=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,title,text,"rich"){Document=document};
        var workspace=new EditingWorkspace(TimeProvider.System,new(10,Guid.NewGuid(),[stored]));
        try{return RichParagraphPdfCapture.Capture(workspace.Notes.ToArray())[0];}finally{workspace.Clear();}
    }
    private static StyledDocument RichRuns(params object[] runs)=>new(1,JsonSerializer.Serialize(new{nodes=new[]{new{type="paragraph",runs}}}));
    private static TextDecorationCollection RichReferenceDecorations(bool underline,bool strike)
    {
        var result=new TextDecorationCollection();if(underline)result.Add(TextDecorations.Underline[0]);if(strike)result.Add(TextDecorations.Strikethrough[0]);return result;
    }
    // Independent native oracle: manually specified WPF Run values, no product mapper, auditor or compositor.
    private static TextBlock RichReferenceBlock(params Run[] runs)
    {
        var block=new TextBlock{FontFamily=new FontFamily("Segoe UI"),FontSize=14,Foreground=Brushes.Black,Background=Brushes.Transparent,
            LineHeight=24,LineStackingStrategy=LineStackingStrategy.MaxHeight,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.LeftToRight,
            Language=XmlLanguage.GetLanguage("ko-KR"),Width=RichReferenceWidth,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
            Margin=new Thickness(RichReferenceMargin,RichReferenceMargin,0,0)};
        foreach(var run in runs)block.Inlines.Add(run);return block;
    }
    private static TextBlock RichBasicReference()=>RichReferenceBlock(new Run("합성 "),
        new Run("굵은 한글 ABC"){FontFamily=new FontFamily("Malgun Gothic"),FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(204,0,0)),
            Background=new SolidColorBrush(Color.FromArgb(128,255,255,0)),TextDecorations=RichReferenceDecorations(true,true)},new Run(" é 😀"));
    private static RichRasterFixture[] RichViewerFixtures()
    {
        string wrap=string.Concat(Enumerable.Repeat("합성 wrap gy ABC  ",24));
        return [new("basic",new(1,RichBasicJson),RichBasicReference),
            new("large",RichRuns(new{text="큰 gy",fontFamily="Malgun Gothic",bold=true,fontSize=96,underline=true,strike=true,foreground="#802000C0",background="#80FFCC00"}),
                ()=>RichReferenceBlock(new Run("큰 gy"){FontFamily=new FontFamily("Malgun Gothic"),FontWeight=FontWeights.Bold,FontSize=96,
                    Foreground=new SolidColorBrush(Color.FromArgb(128,32,0,192)),Background=new SolidColorBrush(Color.FromArgb(128,255,204,0)),TextDecorations=RichReferenceDecorations(true,true)})),
            new("wrap",RichRuns(new{text=wrap,fontFamily="Malgun Gothic",fontSize=24,underline=true,foreground="#006030",background="#402080FF"}),
                ()=>RichReferenceBlock(new Run(wrap){FontFamily=new FontFamily("Malgun Gothic"),FontSize=24,Foreground=new SolidColorBrush(Color.FromRgb(0,96,48)),
                    Background=new SolidColorBrush(Color.FromArgb(64,32,128,255)),TextDecorations=RichReferenceDecorations(true,false)}))];
    }
    private static byte[] RichReferenceRgb(Func<TextBlock> create,Action<string,byte[]>? observe=null)
    {
        Grid? page=null;TextBlock? block=null;RenderTargetBitmap? bitmap=null;byte[]? bgra=null,rgb=null;bool transferred=false;
        try
        {
            // Allocate the return candidate first. Later allocation/native/observer failures must still erase it.
            rgb=new byte[794*1123*3];Array.Fill(rgb,(byte)255);observe?.Invoke("rgb-allocated",rgb);
            page=new Grid{Width=794,Height=1123,Background=Brushes.White};block=create();page.Children.Add(block);
            TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);
            page.Measure(new Size(794,1123));page.Arrange(new Rect(0,0,794,1123));page.UpdateLayout();
            bitmap=new RenderTargetBitmap(794,1123,96,96,PixelFormats.Pbgra32);bitmap.Render(page);bgra=new byte[794*1123*4];bitmap.CopyPixels(bgra,794*4,0);observe?.Invoke("bgra-populated",bgra);
            for(int i=0,j=0;i<bgra.Length;i+=4,j+=3)
            {
                int alpha=bgra[i+3];Require(bgra[i]<=alpha&&bgra[i+1]<=alpha&&bgra[i+2]<=alpha,"Independent rich reference premultiplied channels valid");
                double coverage=alpha/255.0;
                for(int channel=0;channel<3;channel++){double straight=alpha==0?0:bgra[i+2-channel]/coverage;rgb[j+channel]=(byte)Math.Round(straight*coverage+255*(1-coverage),MidpointRounding.AwayFromZero);}
            }
            observe?.Invoke("rgb-populated",rgb);transferred=true;return rgb;
        }
        finally
        {
            if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);
            try{try{bitmap?.Clear();}finally{try{block?.Inlines.Clear();}finally{page?.Children.Clear();}}}
            catch{if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);throw;}
            finally{if(!transferred&&rgb is not null)CryptographicOperations.ZeroMemory(rgb);}
        }
    }
    private static async Task PdfRichParagraphRendererRun()
    {
        Require(typeof(PdfVisualRenderer).GetMethod("RenderParagraphsAsync",BindingFlags.Static|BindingFlags.NonPublic) is not null,"N02 rich native renderer API missing: RenderParagraphsAsync");
        foreach(var fixture in RichViewerFixtures())await RichAssertReference(fixture);
        // Each isolated style loses its oracle comparison if the corresponding production property is dropped.
        var styles=new RichRasterFixture[]{
            new("regular",RichRuns(new{text="ABC gy 한글 é 😀"}),()=>RichReferenceBlock(new Run("ABC gy 한글 é 😀"))),
            new("bold",RichRuns(new{text="ABC gy",bold=true}),()=>RichReferenceBlock(new Run("ABC gy"){FontWeight=FontWeights.Bold})),
            new("underline",RichRuns(new{text="ABC gy",underline=true}),()=>RichReferenceBlock(new Run("ABC gy"){TextDecorations=RichReferenceDecorations(true,false)})),
            new("strike",RichRuns(new{text="ABC gy",strike=true}),()=>RichReferenceBlock(new Run("ABC gy"){TextDecorations=RichReferenceDecorations(false,true)})),
            new("family",RichRuns(new{text="ABC 한글",fontFamily="Malgun Gothic"}),()=>RichReferenceBlock(new Run("ABC 한글"){FontFamily=new FontFamily("Malgun Gothic")})),
            new("size8",RichRuns(new{text="ABC gy",fontSize=8}),()=>RichReferenceBlock(new Run("ABC gy"){FontSize=8})),
            new("size96",RichRuns(new{text="ABC gy",fontSize=96}),()=>RichReferenceBlock(new Run("ABC gy"){FontSize=96})),
            new("foreground",RichRuns(new{text="ABC gy",foreground="#802060C0"}),()=>RichReferenceBlock(new Run("ABC gy"){Foreground=new SolidColorBrush(Color.FromArgb(128,32,96,192))})),
            new("background",RichRuns(new{text="ABC gy  ",background="#8020A0FF"}),()=>RichReferenceBlock(new Run("ABC gy  "){Background=new SolidColorBrush(Color.FromArgb(128,32,160,255))}))};
        foreach(var fixture in styles)await RichAssertReference(fixture);
        foreach(var fixture in new RichRasterFixture[]{
            new("split-grapheme",RichRuns(new{text="e"},new{text="́"},new{text=" 😀"}),()=>RichReferenceBlock(new Run("é 😀"))),
            new("split-crlf",RichRuns(new{text=" A  \r"},new{text="\nB\t C\rD\n"}),()=>RichReferenceBlock(new Run(" A  \nB     C\nD\n"))),
            new("empty",RichRuns(new{text=""}),()=>RichReferenceBlock(new Run("")))})await RichAssertReference(fixture);
        await RichFaceChecks();await RichBoundaryChecks();await RichCleanupChecks();RichReferenceCleanupChecks();
        await RichEmitViewerFixtures();
        Console.WriteLine("N02 native rich renderer: independent styles/RGB, face/glyph/ink, boundaries/refusal and owned cleanup checks passed");
    }
    private static async Task RichAssertReference(RichRasterFixture fixture)
    {
        byte[]? expected=null,actual=null,copied=null;
        try
        {
            expected=RichReferenceRgb(fixture.Reference);
            using var prepared=await RichRender([RichSource(fixture.Document)],owned:bytes=>{if(bytes.Length==794*1123*3){Require(copied is null,"Fixed fixture fits one page");copied=(byte[])bytes.Clone();}});
            actual=VisualPdfFirstRgb(prepared.Bytes);Require(copied is not null&&actual.AsSpan().SequenceEqual(copied),"PDF page image is exactly the copied native RGB: "+fixture.Name);
            Require(VisualPdfReferenceMatches(actual,expected)&&VisualPdfWhiteBorder(actual),"Independent manual WPF styled RGB matches without clipping: "+fixture.Name);
        }
        finally{foreach(var bytes in new[]{expected,actual,copied})if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);}
    }
    private static async Task RichFaceChecks()
    {
        var validate=typeof(PdfVisualRenderer).GetMethod("ValidateRichResolvedFont",BindingFlags.Static|BindingFlags.NonPublic)
            ??throw new Exception("Rich actual face audit missing independently of plain regular gate");
        foreach(var approved in new[]{("Segoe UI",false,"segoeui.ttf"),("Segoe UI",true,"segoeuib.ttf"),("Malgun Gothic",false,"malgun.ttf"),("Malgun Gothic",true,"malgunbd.ttf"),("Segoe UI Symbol",false,"seguisym.ttf"),("Segoe UI Emoji",false,"seguiemj.ttf")})
        {
            var requested=new Typeface(new FontFamily(approved.Item1),FontStyles.Normal,approved.Item2?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal);
            if(!requested.TryGetGlyphTypeface(out var face)||face is null)throw new Exception("Approved native face not actually installed");
            Require(face.FamilyNames[CultureInfo.GetCultureInfo("en-US")]==approved.Item1&&Path.GetFileName(face.FontUri.LocalPath).Equals(approved.Item3,StringComparison.OrdinalIgnoreCase)&&face.StyleSimulations==StyleSimulations.None,"Independent exact approved system face inventory");
            validate.Invoke(null,[face,approved.Item2]);
            if(!approved.Item2)
            {
                RichMustRefuse(()=>validate.Invoke(null,[face,true]),"Actual regular face cannot satisfy bold");
                var simulated=new GlyphTypeface(face.FontUri,StyleSimulations.BoldSimulation);RichMustRefuse(()=>validate.Invoke(null,[simulated,false]),"Actual simulation refused");
            }
        }
        foreach(string family in new[]{"Segoe UI Symbol","Segoe UI Emoji"})
        {
            await RichMustRefuseAsync(()=>RichRender([RichSource(RichRuns(new{text="ABC",fontFamily=family,bold=true}))]),"Unapproved actual bold fallback/simulation refused");
            await RichMustRefuseAsync(()=>RichRender([RichSource(RichRuns(new{text="valid"},new{text="",fontFamily=family,bold=true}))]),"Empty unsupported bold run is still refused");
        }
        if(!new Typeface("Arial").TryGetGlyphTypeface(out var unknown))throw new Exception("Installed Arial negative control missing");
        RichMustRefuse(()=>validate.Invoke(null,[unknown,false]),"Actual unknown family refused");
        foreach(string uri in new[]{"file:///C:/Users/synthetic/Fonts/segoeui.ttf","file://synthetic-server/share/segoeui.ttf","https://example.invalid/segoeui.ttf"})RichMustRefuse(()=>PdfVisualRenderer.ValidateFontOrigin(new Uri(uri)),"Rogue font origin refused without loading");
        foreach(var document in new[]{RichRuns(new{text="e"},new{text="́",bold=true}),RichRuns(new{text="\r"},new{text="\n",underline=true}),RichRuns(new{text="👩‍💻"}),RichRuns(new{text="x",fontFamily="Arial"})})
            RichMustRefuse(()=>RichSource(document),"Unsupported profile is refused before native preparation");
    }
    private static async Task RichBoundaryChecks()
    {
        string line="last-line é gy";string body=string.Join('\n',Enumerable.Repeat(line,43));
        using(var prepared=await RichRender([RichSource(RichRuns(new{text=body,underline=true,background="#40FFCC00"}))]))
        {
            Require(Encoding.Latin1.GetString(prepared.Bytes).Contains("/Count 2 ",StringComparison.Ordinal),"43rd rich minimum-height line moves to next page");
            for(int page=0;page<2;page++)
            {
                byte[] actual=VisualPdfFirstRgb(prepared.Bytes,page),reference=RichReferenceRgb(()=>RichReferenceBlock(new Run(string.Join('\n',Enumerable.Repeat(line,page==0?42:1))){TextDecorations=RichReferenceDecorations(true,false),Background=new SolidColorBrush(Color.FromArgb(64,255,204,0))}));
                try{Require(VisualPdfReferenceMatches(actual,reference)&&VisualPdfWhiteBorder(actual),"Complete last line decoration/background remains visible: "+page);}
                finally{CryptographicOperations.ZeroMemory(actual);CryptographicOperations.ZeroMemory(reference);}
            }
        }
        // Compare a mixed plain note page byte-for-byte to its original renderer, including title spacing.
        var plain=new RichParagraphPdfSource("Plain title"," A  \r\nB\tC ","plain",null,ImmutableArray<RichPdfParagraph>.Empty);
        using(var baseline=await PdfVisualRenderer.RenderPlainAsync([new(plain.Title,plain.Text)],()=>true,CancellationToken.None))
        using(var mixed=await RichRender([plain,RichSource(new(1,RichBasicJson))]))
        {
            byte[] first=VisualPdfFirstRgb(baseline.Bytes),second=VisualPdfFirstRgb(mixed.Bytes);try{Require(first.AsSpan().SequenceEqual(second),"Mixed selection retains byte-exact original plain page layout");}finally{CryptographicOperations.ZeroMemory(first);CryptographicOperations.ZeroMemory(second);}
        }
        await RichMustRefuseAsync(()=>RichRender([RichSource(RichRuns(new{text=new string('\n',42*256)}))]),"257-page source refused as a whole");
        // Closed geometry helper is exercised with actual native drawing bounds by every positive fixture;
        // adversarial finite rectangles exercise its otherwise unreachable (8..96 DIP) hard page limits.
        var geometry=typeof(PdfVisualRenderer).GetMethod("RichLineGeometry",BindingFlags.Static|BindingFlags.NonPublic)
            ??throw new Exception("Rich complete-line geometry gate missing");
        RichMustRefuse(()=>geometry.Invoke(null,[24d,100d,new Rect(0,0,900,24)]),"Too-wide complete drawing ink refused");
        RichMustRefuse(()=>geometry.Invoke(null,[24d,100d,new Rect(0,-1100,100,1200)]),"Too-tall complete drawing ink refused");
    }
    private static async Task RichCleanupChecks()
    {
        var owned=new List<byte[]>();int populated=0;byte[]? candidate=null;
        void Observe(byte[] bytes){owned.Add(bytes);if(bytes.Any(value=>value!=0))populated++;if(bytes.AsSpan().StartsWith("%PDF-"u8))candidate=bytes;}
        using(var result=await RichRender([RichSource(new(1,RichBasicJson))],owned:Observe))
        {
            Require(candidate is not null&&ReferenceEquals(candidate,result.Bytes)&&candidate.Any(value=>value!=0),"Populated completed candidate observed and retained until consumer disposal");
            Require(populated>=3&&owned.Where(bytes=>!ReferenceEquals(bytes,candidate)).All(bytes=>bytes.All(value=>value==0)),"Populated BGRA/RGB, compression and builder buffers erased after native settlement");
        }
        Require(owned.All(bytes=>bytes.All(value=>value==0)),"Consumer disposal erases the final observed candidate");
        foreach(string failAt in new[]{"bgra","rgb","candidate"})
        {
            owned.Clear();bool failed=false;
            try{using var unused=await RichRender([RichSource(new(1,RichBasicJson))],owned:bytes=>{Observe(bytes);if(failAt=="bgra"&&bytes.Length==794*1123*4||failAt=="rgb"&&bytes.Length==794*1123*3||failAt=="candidate"&&bytes.AsSpan().StartsWith("%PDF-"u8))throw new InvalidOperationException("synthetic observer failure");});}
            catch(InvalidOperationException error)when(error.Message=="synthetic observer failure"){failed=true;}
            Require(failed&&owned.Count>0&&owned.All(bytes=>bytes.All(value=>value==0)),"Throwing observer erases actual owned buffers: "+failAt);
        }
        owned.Clear();await RichMustRefuseAsync(()=>RichRender([RichSource(new(1,RichBasicJson)),RichSource(RichRuns(new{text="late",fontFamily="Segoe UI Symbol",bold=true}))],owned:Observe),"Late native font refusal discards earlier page");
        Require(owned.Any(bytes=>bytes.Length==794*1123*3)&&owned.All(bytes=>bytes.All(value=>value==0)),"Late refusal zeroes prior actual page and builder allocations");
        owned.Clear();using var cancel=new CancellationTokenSource();
        var pending=RichRender([RichSource(new(1,RichBasicJson)),RichSource(RichRuns(new{text=string.Join('\n',Enumerable.Repeat("합성 cancel",100))}))],token:cancel.Token,owned:Observe);
        Require(!pending.IsCompleted,"Native work reaches bounded page yield before cancellation");cancel.Cancel();bool canceled=false;
        try{using var unused=await pending;}catch(OperationCanceledException){canceled=true;}
        Require(canceled&&pending.IsCompleted&&owned.Count>0&&owned.All(bytes=>bytes.All(value=>value==0)),"Canceled real native task settles and erases earlier populated page/builder bytes");
        using var batchCancel=new CancellationTokenSource();var batched=RichRender([RichSource(RichRuns(new{text=string.Join('\n',Enumerable.Repeat("batch é",100))}))],token:batchCancel.Token);
        Require(!batched.IsCompleted,"32-line bounded native batch yields before page completion");batchCancel.Cancel();try{using var unused=await batched;throw new Exception("Missing batch cancellation");}catch(OperationCanceledException){}
        bool current=true;var revoked=RichRender([RichSource(RichRuns(new{text=string.Join('\n',Enumerable.Repeat("current",100))}))],()=>current);current=false;try{using var unused=await revoked;throw new Exception("Missing authority cancellation");}catch(OperationCanceledException){}
    }
    private static void RichReferenceCleanupChecks()
    {
        foreach(string phase in new[]{"bgra-populated","rgb-populated"})
        {
            var owned=new List<byte[]>();bool failed=false;
            try{byte[] unused=RichReferenceRgb(RichBasicReference,(name,bytes)=>{owned.Add(bytes);if(name==phase)throw new InvalidOperationException("synthetic reference failure");});CryptographicOperations.ZeroMemory(unused);}
            catch(InvalidOperationException error)when(error.Message=="synthetic reference failure"){failed=true;}
            Require(failed&&owned.Count>=2&&owned.All(bytes=>bytes.All(value=>value==0)),"Independent reference erases earlier RGB on later native/observer failure: "+phase);
        }
    }
    private static void RichMustRefuse(Action operation,string assertion)
    {
        bool refused=false;try{operation();}catch(TargetInvocationException error)when(error.InnerException is InvalidDataException or IOException){refused=true;}catch(InvalidDataException){refused=true;}catch(IOException){refused=true;}
        Require(refused,assertion);
    }
    private static async Task RichMustRefuseAsync(Func<Task<PreparedTextExport>> operation,string assertion)
    {bool refused=false;try{using var unused=await operation();}catch(InvalidDataException){refused=true;}Require(refused,assertion);}
    private static async Task RichEmitViewerFixtures()
    {
        string? output=Environment.GetEnvironmentVariable("MEMO_N02_VIEWER_OUTPUT");if(string.IsNullOrEmpty(output))return;
        Directory.CreateDirectory(output);var manifest=new List<object>();
        foreach(var fixture in RichViewerFixtures())
        {
            byte[] reference=RichReferenceRgb(fixture.Reference);byte[]? actual=null;
            try
            {
                using var pdf=await RichRender([RichSource(fixture.Document)]);actual=VisualPdfFirstRgb(pdf.Bytes);
                Require(Encoding.Latin1.GetString(pdf.Bytes).Contains("/Count 1 ",StringComparison.Ordinal)&&VisualPdfReferenceMatches(actual,reference),"Viewer reference bound to actual fixed synthetic PDF page: "+fixture.Name);
                File.WriteAllBytes(Path.Combine(output,fixture.Name+".pdf"),pdf.Bytes);File.WriteAllBytes(Path.Combine(output,fixture.Name+".rgb"),reference);
                manifest.Add(new{name=fixture.Name,pdf_sha256=Convert.ToHexString(SHA256.HashData(pdf.Bytes)).ToLowerInvariant(),pdf_bytes=pdf.Bytes.Length,reference_sha256=Convert.ToHexString(SHA256.HashData(reference)).ToLowerInvariant(),reference_bytes=reference.Length});
            }
            finally{CryptographicOperations.ZeroMemory(reference);if(actual is not null)CryptographicOperations.ZeroMemory(actual);}
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{version=1,width=794,height=1123,dpi=96,fixtures=manifest},new JsonSerializerOptions{WriteIndented=true}));
    }
}

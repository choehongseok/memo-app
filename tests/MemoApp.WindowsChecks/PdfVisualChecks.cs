using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Core.Transfer;
using MemoApp.Windows;

internal static partial class Program
{
    private static async Task PdfVisualRun()
    {
        foreach(var vector in new(byte[] Pbgra,byte[] Expected)[]{([0,0,0,0],[255,255,255]),([0,1,1,1],[255,255,254]),([0,64,128,128],[255,191,127]),([20,100,200,254],[201,101,21]),([20,100,200,255],[200,100,20])})
        {
            byte[] rgb=[0,0,0];PdfVisualRenderer.CompositeWhiteRgb(vector.Pbgra,rgb);
            Require(rgb.SequenceEqual(vector.Expected),"Premultiplied raster white composition handles exact alpha0/1/128/254/255 without multiplying alpha twice");
        }
        byte[] untouched=[7,8,9];bool invalidPremultiplied=false;
        try{PdfVisualRenderer.CompositeWhiteRgb([0,129,0,128],untouched);}catch(InvalidDataException){invalidPremultiplied=true;}
        Require(invalidPremultiplied&&untouched.SequenceEqual(new byte[]{7,8,9}),"Invalid premultiplied channels are refused before writing opaque RGB");
        using(var choices=new PdfExportModeChoiceWindow(()=>true))
        {
            Require(choices.FindName("TextModeChoice") is RadioButton text&&text.Content.ToString()!.Contains("검색/복사")&&choices.FindName("VisualModeChoice") is RadioButton visual&&visual.Content.ToString()!.Contains("미지원"),"Native PDF dialog presents both explicit capabilities");
            Require(choices.SelectedMode is null&&choices.FindName("CancelButton") is Button{IsCancel:true},"PDF mode dialog requires explicit continuation and supports cancellation");
        }
        foreach(string text in new[]{"한글 漢字 ABC","e\u0301","\u1100\u1161","😀","  A  B  ","A\r\nB\rC\n\nD\tE",new string('W',240)+"e\u0301",""})
        {
            string actualRasterAudit="";
            using var prepared=await PdfVisualRenderer.RenderPlainAsync([new("",text)],()=>true,CancellationToken.None,
                buffer=>{if(buffer.Length==794*1123*4)actualRasterAudit=VisualPdfBgraAudit(buffer);});
            byte[]? rgb=null,reference=null;
            try
            {
                rgb=VisualPdfFirstRgb(prepared.Bytes);reference=VisualPdfTextBlockReference(text,out string referenceAudit,rgb);
                bool matches=rgb.Length==794*1123*3&&VisualPdfReferenceMatches(rgb,reference);
                if(!matches)Console.WriteLine(VisualPdfSyntheticDiagnostic(rgb,reference,text)+" actualRaster="+actualRasterAudit+" reference="+referenceAudit);
                Require(matches,"Native TextFormatter/glyph-audited PDF pixels match independent TextBlock: "+Convert.ToHexString(Encoding.UTF8.GetBytes(text)));
                string grammar=Encoding.Latin1.GetString(prepared.Bytes);Require(grammar.Contains("/Subtype /Image")&&grammar.Contains("/DeviceRGB")&&!grammar.Contains("/ToUnicode")&&!grammar.Contains("/Type /Font"),"Display PDF contains raster pages and no searchable text/font objects");
            }
            finally{if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);if(reference is not null)CryptographicOperations.ZeroMemory(reference);}
        }
        foreach(string invalid in new[]{"\ud800","\0","\u0001","אב","مرحبا","👩\u200d💻","☀\ufe0f","🇰🇷","👍🏽","e"+new string('\u0301',129)})
        {
            bool rejected=false;try{using var unused=await PdfVisualRenderer.RenderPlainAsync([new("",invalid)],()=>true,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
            Require(rejected,"Unverified shaping/invalid Unicode/oversized grapheme explicitly refused");
        }
        foreach(string origin in new[]{"https://example.invalid/font.ttf","file:///C:/Users/synthetic/font.ttf","file://synthetic-server/share/font.ttf"})
        {
            bool rejected=false;try{PdfVisualRenderer.ValidateFontOrigin(new Uri(origin));}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}
            Require(rejected,"Unexpected or remote resolved font origin refused without loading it");
        }
        if(!new Typeface("Arial").TryGetGlyphTypeface(out var unexpected)||unexpected is null)throw new Exception("Independent native unexpected-family fixture must resolve installed Arial");
        PdfVisualRenderer.ValidateFontOrigin(unexpected.FontUri);
        bool familyRefused=false;try{PdfVisualRenderer.ValidateResolvedFont(unexpected);}catch(InvalidDataException){familyRefused=true;}
        Require(familyRefused,"Actual installed Windows font outside the fixed family/face/filename whitelist is refused");
        var owned=new List<byte[]>();int observedPopulated=0;using(var complete=await PdfVisualRenderer.RenderPlainAsync([new("","합성 e\u0301")],()=>true,CancellationToken.None,bytes=>{owned.Add(bytes);if(bytes.Any(value=>value!=0))observedPopulated++;}))
            Require(owned.Count==2&&observedPopulated==2&&owned.All(bytes=>bytes.All(value=>value==0)),"Successful native renderer zeroes every populated owned BGRA/RGB page array");
        owned.Clear();observedPopulated=0;bool observedFailure=false;
        try{using var unused=await PdfVisualRenderer.RenderPlainAsync([new("","실패 합성 ABC")],()=>true,CancellationToken.None,bytes=>{owned.Add(bytes);if(bytes.Any(value=>value!=0))observedPopulated++;if(owned.Count==2)throw new InvalidOperationException("Synthetic owned RGB allocation failure");});}
        catch(InvalidOperationException exception)when(exception.Message=="Synthetic owned RGB allocation failure"){observedFailure=true;}
        Require(observedFailure&&owned.Count==2&&observedPopulated==2&&owned.All(bytes=>bytes.All(value=>value==0)),"Renderer failure zeroes all already populated owned page buffers");owned.Clear();
        string[] boundaryLines=Enumerable.Repeat("last-line e\u0301 descender gy",43).ToArray();
        using(var boundary=await PdfVisualRenderer.RenderPlainAsync([new("",string.Join('\n',boundaryLines))],()=>true,CancellationToken.None))
        {
            Require(Encoding.Latin1.GetString(boundary.Bytes).Contains("/Count 2 "),"Native fixed line/ink layout paginates the 43rd line to a second page");
            for(int page=0;page<2;page++)
            {
                byte[]? actual=null,reference=null;
                try{actual=VisualPdfFirstRgb(boundary.Bytes,page);reference=VisualPdfTextBlockReference(string.Join('\n',page==0?boundaryLines.Take(42):boundaryLines.Skip(42)));Require(VisualPdfReferenceMatches(actual,reference)&&VisualPdfWhiteBorder(actual),"Last full-page line and next-page ink match native reference without edge clipping: "+page);}
                finally{if(actual is not null)CryptographicOperations.ZeroMemory(actual);if(reference is not null)CryptographicOperations.ZeroMemory(reference);}
            }
        }
        bool overPages=false;try{using var unused=await PdfVisualRenderer.RenderPlainAsync([new("",new string('\n',42*256))],()=>true,CancellationToken.None);}catch(InvalidDataException){overPages=true;}
        Require(overPages,"Explicit newline document exceeding 256 pages is refused");
        using(var cancellation=new CancellationTokenSource())
        {
            var pending=PdfVisualRenderer.RenderPlainAsync([new("",string.Join('\n',Enumerable.Repeat("긴 합성 ABC",100)))],()=>true,cancellation.Token);cancellation.Cancel();
            bool canceled=false;try{using var unused=await pending;}catch(OperationCanceledException){canceled=true;}Require(canceled,"Renderer cancellation at a bounded line yield discards its complete candidate");
        }
        await PdfVisualAuthorityChecks();
    }

    private static async Task PdfVisualAuthorityChecks()
    {
        string dir=Path.Combine(Path.GetTempPath(),"memo-wpf-pdf-visual-"+Guid.NewGuid().ToString("N")),root=Path.Combine(dir,"vault");Directory.CreateDirectory(dir);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();MainWindow? main=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));var active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var note=active.Workspace.CreateNote();note.Title="합성 표시 PDF";note.Text="한글 e\u0301 😀";var other=active.Workspace.CreateNote();other.Text="다른 합성";
            Invoke(main,"RefreshNotes",note);await Idle();await Field<Task>(main,"recentTask");var list=Control<ListBox>(main,"NotesList");list.SelectedItem=note;Require(await active.SaveAsync(),"Visual PDF native baseline saved");await Idle();list.SelectedItem=note;
            var method=typeof(MainWindow).GetMethod("ExportSelectedVisualPdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            Task<bool> Export(string name,Func<bool>? confirm=null,Func<string?>? choose=null,IAtomicVaultFiles? files=null)=>(Task<bool>)method.Invoke(main,[confirm??(()=>true),choose??(()=>Path.Combine(dir,name)),files])!;
            void RoundTrip(){list.SelectedItem=other;list.SelectedItem=note;}
            Require(!await Export("declined.pdf",()=>false,()=>throw new Exception("No visual picker without consent"))&&!File.Exists(Path.Combine(dir,"declined.pdf")),"Visual PDF default-No consent can cancel");
            Require(!await Export("picker.pdf",choose:()=>null),"Visual PDF destination can cancel");
            Require(!await Export("selection.pdf",()=>{RoundTrip();return true;})&&!File.Exists(Path.Combine(dir,"selection.pdf")),"Visual PDF consent selection round trip permanently revokes");
            Require(!await Export("inside.pdf",choose:()=>Path.Combine(root,"inside.pdf"))&&!File.Exists(Path.Combine(root,"inside.pdf")),"Visual PDF refuses the active vault directory");
            Require(!await Export("wrong.txt")&&!File.Exists(Path.Combine(dir,"wrong.txt")),"Visual PDF requires a PDF destination");
            var modeMethod=typeof(MainWindow).GetMethod("ExportSelectedPdfWithModeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            bool textConsent=false,visualConsent=false;
            Task<bool> Mode(Func<PdfExportMode?> chooseMode,string name)=>(Task<bool>)modeMethod.Invoke(main,[chooseMode,(Func<bool>)(()=>{textConsent=true;return true;}),(Func<bool>)(()=>{visualConsent=true;return true;}),(Func<string?>)(()=>Path.Combine(dir,name)),null])!;
            Require(!await Mode(()=>null,"mode-cancel.pdf")&&!textConsent&&!visualConsent,"Mode cancellation never asks plaintext consent");
            Require(!await Mode(()=>{RoundTrip();return PdfExportMode.Visual;},"stale-mode.pdf")&&!textConsent&&!visualConsent&&!File.Exists(Path.Combine(dir,"stale-mode.pdf")),"Selection round trip during mode dialog refuses both routes");
            note.Text="합성 ABC";await Idle();list.SelectedItem=note;
            Require(await Mode(()=>PdfExportMode.Text,"explicit-text.pdf")&&textConsent&&!visualConsent,"Explicit text mode retains the original searchable PDF route");textConsent=visualConsent=false;
            foreach(string unsupported in new[]{"markdown","rich"})
            {
                active.Workspace.ConvertMode(note,unsupported,true);await Idle();list.SelectedItem=note;
                Require(!await Export(unsupported+".pdf")&&!File.Exists(Path.Combine(dir,unsupported+".pdf")),"Visual PDF refuses "+unsupported+" before creating a file");
                active.Workspace.ConvertMode(note,"plain",true);await Idle();list.SelectedItem=note;
            }
            note.Text="한글 e\u0301 😀";await Idle();list.SelectedItem=note;Require(await active.SaveAsync(),"Visual current baseline settled");await Idle();list.SelectedItem=note;
            byte[] snapshot=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()),cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));
            try
            {
                Require(await Mode(()=>PdfExportMode.Visual,"visual.pdf")&&visualConsent&&!textConsent,"Explicit visual mode writes display PDF");
                Require(snapshot.SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(active.Workspace.Capture()))&&cipher.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"current.vault"))),"Visual export preserves exact source and ciphertext");
                byte[] saved=File.ReadAllBytes(Path.Combine(dir,"visual.pdf"));try{Require(!await Export("visual.pdf")&&saved.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"visual.pdf"))),"Visual PDF never overwrites an existing file");}finally{CryptographicOperations.ZeroMemory(saved);}
            }
            finally{CryptographicOperations.ZeroMemory(snapshot);CryptographicOperations.ZeroMemory(cipher);}
            foreach(string mutation in new[]{"edit","selection","accept"})
            {
                note.Text=string.Join('\n',Enumerable.Repeat("합성 긴 ABC",100));await Idle();list.SelectedItem=note;
                var pending=Export("render-"+mutation+".pdf");Require(!pending.IsCompleted,"Visual preparation yields before file creation");
                if(mutation=="edit")note.Title+="x";else if(mutation=="selection")RoundTrip();else active.Workspace.AcceptPrepared(active.Workspace.Capture());
                Require(!await pending&&!File.Exists(Path.Combine(dir,"render-"+mutation+".pdf")),"Mid-render "+mutation+" revocation discards entire candidate");
            }
            note.Text="합성 ABC";await Idle();list.SelectedItem=note;
            foreach(string mutation in new[]{"edit","selection","accept"})
            {
                var paused=new PausedBatchFiles();var pending=Export("write-"+mutation+".pdf",files:paused);
                try
                {
                    await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    if(mutation=="edit")note.Title+="x";else if(mutation=="selection")RoundTrip();else active.Workspace.AcceptPrepared(active.Workspace.Capture());
                    paused.Continue.TrySetResult();Require(!await pending&&new FileInfo(Path.Combine(dir,"write-"+mutation+".pdf")).Length==0,"Paused visual CreateNew "+mutation+" revocation writes no plaintext");
                }
                finally{paused.Continue.TrySetResult();await pending;}
                await Idle();list.SelectedItem=note;
            }
            var closures=typeof(MainWindow).GetNestedTypes(BindingFlags.NonPublic).Where(t=>t.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Any(m=>m.Name.StartsWith("<WriteVisualPdfExportAsync>",StringComparison.Ordinal))).ToArray();
            var allowed=new[]{typeof(PreparedTextExport),typeof(string),typeof(CancellationToken),typeof(IAtomicVaultFiles)};
            Require(closures.Length==1&&closures[0].GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).All(f=>allowed.Contains(f.FieldType)),"Compiled visual PDF worker excludes UI/source/session/key owners");
            var lockPaused=new PausedBatchFiles();var lockExport=Export("locked.pdf",files:lockPaused);
            try
            {
                await lockPaused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));var locking=active.LockAsync();Require(active.KeysReleased&&!lockExport.IsCompleted,"Visual PDF paused file work does not delay key release");
                lockPaused.Continue.TrySetResult();Require(!await lockExport&&new FileInfo(Path.Combine(dir,"locked.pdf")).Length==0,"Lock revokes paused visual file write");await locking;
            }
            finally{lockPaused.Continue.TrySetResult();await lockExport;}
        }
        finally
        {
            if(main is not null){var active=Field<SaveCoordinator?>(main,"session");if(active is not null&&!active.IsLocked)await active.LockAsync();Invoke(main,"ReleaseSettledSession");SetField(main,"confirmedExit",true);main.Close();}
            CryptographicOperations.ZeroMemory(secret);Directory.Delete(dir,true);
        }
    }

    private static byte[] VisualPdfFirstRgb(byte[] pdf,int page=0)
    {
        string grammar=Encoding.Latin1.GetString(pdf);int image=-1;for(int i=0;i<=page;i++)image=grammar.IndexOf("/Subtype /Image",image+1,StringComparison.Ordinal);
        Require(image>=0,"Requested visual PDF raster page exists");int lengthStart=grammar.IndexOf("/Length ",image,StringComparison.Ordinal)+8;
        int lengthEnd=grammar.IndexOf(' ',lengthStart),length=int.Parse(grammar[lengthStart..lengthEnd],System.Globalization.CultureInfo.InvariantCulture),start=grammar.IndexOf("stream\n",lengthEnd,StringComparison.Ordinal)+7;
        using var input=new MemoryStream(pdf,start,length,false);using var decoder=new ZLibStream(input,CompressionMode.Decompress);byte[] rgb=new byte[794*1123*3];decoder.ReadExactly(rgb);Require(decoder.ReadByte()==-1,"Visual PDF exact fixed raster dimensions");return rgb;
    }
    private static byte[] VisualPdfTextBlockReference(string text)=>VisualPdfTextBlockReference(text,out _);
    private static byte[] VisualPdfTextBlockReference(string text,out string audit,byte[]? actual=null)
    {
        string layout=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\t","    ",StringComparison.Ordinal);
        var page=new Grid{Width=794,Height=1123,Background=Brushes.White};
        var reference=new TextBlock{Text=layout,FontFamily=new FontFamily("Global User Interface"),FontSize=11*96.0/72,LineHeight=24,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.LeftToRight,Foreground=Brushes.Black,Width=210*96.0/25.4-2*40*96.0/72,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(40*96.0/72,40*96.0/72,0,0),Language=System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR")};
        TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);page.Children.Add(reference);page.Measure(new Size(794,1123));page.Arrange(new Rect(0,0,794,1123));page.UpdateLayout();
        var bitmap=new RenderTargetBitmap(794,1123,96,96,PixelFormats.Pbgra32);byte[]? bgra=null,rgb=null;bool transferred=false;
        try
        {
            bitmap.Render(page);bgra=new byte[794*1123*4];rgb=new byte[794*1123*3];
            audit=VisualPdfReferenceAudit(page,reference);
            bitmap.CopyPixels(bgra,794*4,0);
            audit+=" raster="+VisualPdfBgraAudit(bgra);
            foreach(int corner in new[]{0,(794-1)*4,(1123-1)*794*4,bgra.Length-4})Require(bgra[corner]==255&&bgra[corner+1]==255&&bgra[corner+2]==255&&bgra[corner+3]==255,"Independent native reference has an opaque white page background");
            for(int i=0,j=0;i<bgra.Length;i+=4,j+=3)
            {
                int alpha=bgra[i+3];Require(bgra[i]<=alpha&&bgra[i+1]<=alpha&&bgra[i+2]<=alpha,"Independent native reference has valid premultiplied channels");
                // Independent floating equation: recover straight color, then composite over an opaque white canvas.
                // The renderer's integer conversion is not called by this TextBlock reference.
                double coverage=alpha/255.0;
                for(int channel=0;channel<3;channel++)
                {
                    double straight=alpha==0?0:bgra[i+2-channel]/coverage;
                    rgb[j+channel]=(byte)Math.Round(straight*coverage+255*(1-coverage),MidpointRounding.AwayFromZero);
                }
            }
            if(text=="😀"&&actual is not null&&!VisualPdfReferenceMatches(actual,rgb))
            {
                audit+=" referenceDrawingReplay="+VisualPdfReferenceDrawingReplay(reference,rgb);
                audit+=" liveControls="+VisualPdfLiveReferenceControls(page,reference,rgb);
            }
            transferred=true;return rgb;
        }
        finally
        {
            if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);
            try{try{bitmap.Clear();}finally{page.Children.Clear();}}
            catch{if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);throw;}
            finally{if(!transferred&&rgb is not null)CryptographicOperations.ZeroMemory(rgb);}
        }
    }
    // Bounded summaries of borrowed fixed-synthetic pixels; retain no raster bytes.
    private static string VisualPdfBgraAudit(byte[] bgra)
    {
        int minAlpha=255,below255=0,invalidPremultiplied=0;var histogram=new int[16];
        for(int i=0;i<bgra.Length;i+=4)
        {
            int alpha=bgra[i+3];minAlpha=Math.Min(minAlpha,alpha);
            if(alpha<255){below255++;histogram[alpha/16]++;}
            if(bgra[i]>alpha||bgra[i+1]>alpha||bgra[i+2]>alpha)invalidPremultiplied++;
        }
        string Pixel(int x,int y){int i=(y*794+x)*4;return $"({x},{y})=[{bgra[i]},{bgra[i+1]},{bgra[i+2]},{bgra[i+3]}]";}
        return $"minAlpha={minAlpha} below255={below255} alphaBins16="+string.Join(",",histogram)+$" invalidPremultiplied={invalidPremultiplied} bgra="+Pixel(60,57)+","+Pixel(62,71);
    }
    private static bool VisualPdfReferenceMatches(byte[] actual,byte[] expected)
    {
        long difference=0;int large=0,ink=0;for(int i=0;i<actual.Length;i+=3){if(expected[i]<250||expected[i+1]<250||expected[i+2]<250)ink++;int delta=Math.Max(Math.Abs(actual[i]-expected[i]),Math.Max(Math.Abs(actual[i+1]-expected[i+1]),Math.Abs(actual[i+2]-expected[i+2])));difference+=delta;if(delta>32)large++;}
        return large<=Math.Max(8,ink/20)&&difference<=Math.Max(64,ink*8);
    }
    // Called only by the fixed synthetic fixtures above; never by product/private-note export.
    private static string VisualPdfSyntheticDiagnostic(byte[] actual,byte[] expected,string synthetic)
    {
        static (int Left,int Top,int Right,int Bottom,int Count) Bounds(byte[] pixels)
        {
            int left=794,top=1123,right=-1,bottom=-1,count=0;
            for(int y=0;y<1123;y++)for(int x=0;x<794;x++){int i=(y*794+x)*3;if(pixels[i]>=250&&pixels[i+1]>=250&&pixels[i+2]>=250)continue;left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);count++;}
            return(left,top,right,bottom,count);
        }
        var a=Bounds(actual);var b=Bounds(expected);long sum=0;int large=0,first=-1,peak=0,peakAt=-1;
        for(int i=0;i<actual.Length;i+=3){int delta=0;for(int c=0;c<3;c++)delta=Math.Max(delta,Math.Abs(actual[i+c]-expected[i+c]));sum+=delta;if(delta>32)large++;if(delta>0&&first<0)first=i/3;if(delta>peak){peak=delta;peakAt=i/3;}}
        int left=Math.Max(0,Math.Min(a.Left,b.Left)-4),right=Math.Min(793,Math.Max(a.Right,b.Right)+4),top=Math.Max(0,Math.Min(a.Top,b.Top)-13),bottom=Math.Min(1122,Math.Max(a.Bottom,b.Bottom)+13);
        long best=long.MaxValue;int bestX=0,bestY=0;
        for(int dy=-12;dy<=12;dy++)for(int dx=-3;dx<=3;dx++)
        {
            long score=0;for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
            {int i=(y*794+x)*3,rx=x-dx,ry=y-dy,delta=0;for(int c=0;c<3;c++){int reference=rx is >=0 and <794&&ry is >=0 and <1123?expected[(ry*794+rx)*3+c]:255;delta=Math.Max(delta,Math.Abs(actual[i+c]-reference));}score+=delta;}
            if(score<best){best=score;bestX=dx;bestY=dy;}
        }
        var owner=typeof(PdfVisualRenderer);const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var run=Activator.CreateInstance(owner.GetNestedType("PlainRunProperties",BindingFlags.NonPublic)!,true)!;
        var paragraph=(TextParagraphProperties)Activator.CreateInstance(owner.GetNestedType("PlainParagraphProperties",BindingFlags.NonPublic)!,flags,null,[run],null)!;
        string layout=synthetic.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\t","    ",StringComparison.Ordinal);
        var source=(TextSource)Activator.CreateInstance(owner.GetNestedType("PlainTextSource",BindingFlags.NonPublic)!,flags,null,[layout,run],null)!;
        using var formatter=TextFormatter.Create(TextFormattingMode.Ideal);using var line=formatter.FormatLine(source,0,210*96.0/25.4-2*40*96.0/72,paragraph,null);
        string faces=string.Join(",",line.GetIndexedGlyphRuns().Select(r=>Path.GetFileName(r.GlyphRun.GlyphTypeface.FontUri.LocalPath)).Distinct());
        string glyphs=string.Join(";",line.GetIndexedGlyphRuns().Take(8).Select(r=>VisualPdfGlyphAudit(r.GlyphRun)));
        var ink=(Rect)owner.GetMethod("AuditLine",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[line,layout,0,Math.Min(line.Length,layout.Length)])!;
        double above=ink.IsEmpty?0:Math.Min(0,ink.Top);
        string replay=VisualPdfReplayAudit(line,above,actual);
        return $"SYNTHETIC PDF pixels actualBounds={a} referenceBounds={b} first=({first%794},{first/794}) peak={peak}@({peakAt%794},{peakAt/794}) sum={sum} large={large} bestTranslation=({bestX},{bestY}) translatedSum={best} lineHeight={line.Height:R} baseline={line.Baseline:R} extent={line.Extent:R} overhangLeading={line.OverhangLeading:R} overhangTrailing={line.OverhangTrailing:R} overhangAfter={line.OverhangAfter:R} width={line.WidthIncludingTrailingWhitespace:R} faces={faces} inkTop={ink.Top:R} inkBottom={ink.Bottom:R} drawY={PdfVisualRenderer.Margin-above:R} textSourcePpd={source.PixelsPerDip:R} renderingTier={RenderCapability.Tier} glyphs={glyphs} replay={replay}";
    }
    // Diagnostic data from the exact unchanged independent reference visual; fixed synthetic text only.
    private static string VisualPdfReferenceAudit(Grid page,TextBlock reference)
    {
        var dpi=VisualTreeHelper.GetDpi(reference);var offset=VisualTreeHelper.GetOffset(reference);var origin=reference.TransformToAncestor(page).Transform(new Point(0,0));
        return $"baseline={reference.BaselineOffset:R} visualOffset=({offset.X:R},{offset.Y:R}) origin=({origin.X:R},{origin.Y:R}) dpi=({dpi.DpiScaleX:R},{dpi.DpiScaleY:R},{dpi.PixelsPerDip:R}) desired=({reference.DesiredSize.Width:R},{reference.DesiredSize.Height:R}) render=({reference.RenderSize.Width:R},{reference.RenderSize.Height:R}) layoutRounding={reference.UseLayoutRounding} snap={reference.SnapsToDevicePixels} formatting={TextOptions.GetTextFormattingMode(reference)} rendering={TextOptions.GetTextRenderingMode(reference)} hinting={TextOptions.GetTextHintingMode(reference)} clearType={RenderOptions.GetClearTypeHint(reference)} glyphs="+VisualPdfDrawingAudit(VisualTreeHelper.GetDrawing(reference));
    }
    // A first-line reproduction is accepted as representative only when its entire raster matches.
    private static string VisualPdfReplayAudit(TextLine line,double above,byte[] actual)
    {
        var visual=(DrawingVisual)Activator.CreateInstance(typeof(PdfVisualRenderer).GetNestedType("PlainPageVisual",BindingFlags.NonPublic)!,true)!;
        TextOptions.SetTextFormattingMode(visual,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(visual,TextRenderingMode.Grayscale);
        using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,794,1123));
            line.Draw(context,new Point(PdfVisualRenderer.Margin,PdfVisualRenderer.Margin-above),InvertAxes.None);
        }
        return "exactProductVisual="+visual.GetType().Name+" "+VisualPdfRasterReplayAudit(visual,actual)+$" formatting={TextOptions.GetTextFormattingMode(visual)} rendering={TextOptions.GetTextRenderingMode(visual)} hinting={TextOptions.GetTextHintingMode(visual)} clearType={RenderOptions.GetClearTypeHint(visual)} drawing="+VisualPdfDrawingAudit(VisualTreeHelper.GetDrawing(visual));
    }
    private static string VisualPdfReferenceDrawingReplay(TextBlock reference,byte[] expected)
    {
        ContainerVisual? root=null,offset=null;
        try
        {
        Drawing drawing=VisualTreeHelper.GetDrawing(reference)!;
        var direct=new DrawingVisual();TextOptions.SetTextFormattingMode(direct,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(direct,TextRenderingMode.Grayscale);
        using(var context=direct.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,794,1123));
            context.PushTransform(new TranslateTransform(PdfVisualRenderer.Margin,PdfVisualRenderer.Margin));
            try{context.DrawDrawing(drawing);}finally{context.Pop();}
        }
        root=new ContainerVisual();TextOptions.SetTextFormattingMode(root,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(root,TextRenderingMode.Grayscale);
        var white=new DrawingVisual();using(var context=white.RenderOpen())context.DrawRectangle(Brushes.White,null,new Rect(0,0,794,1123));root.Children.Add(white);
        offset=new ContainerVisual{Offset=new Vector(PdfVisualRenderer.Margin,PdfVisualRenderer.Margin)};
        var child=new DrawingVisual();using(var context=child.RenderOpen())context.DrawDrawing(drawing);offset.Children.Add(child);root.Children.Add(offset);
        return "directTransform={"+VisualPdfRasterReplayAudit(direct,expected)+"} childOffset={"+VisualPdfRasterReplayAudit(root,expected)+"}";
        }
        finally{try{root?.Children.Clear();}finally{offset?.Children.Clear();}}
    }
    // Keep the original pre-inspection reference as the oracle; these controls never replace it.
    private static string VisualPdfLiveReferenceControls(Grid page,TextBlock reference,byte[] expected)
    {
        ContainerVisual? fresh=null;
        try
        {
            Vector originalOffset=VisualTreeHelper.GetOffset(reference);
            string originalFlags=VisualPdfVisualAudit(reference),pageFlags=VisualPdfVisualAudit(page);
            string repeat=VisualPdfRasterReplayAudit(page,expected);
            fresh=new ContainerVisual();TextOptions.SetTextFormattingMode(fresh,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(fresh,TextRenderingMode.Grayscale);
            var white=new DrawingVisual();using(var context=white.RenderOpen())context.DrawRectangle(Brushes.White,null,new Rect(0,0,794,1123));fresh.Children.Add(white);
            page.Children.Remove(reference);fresh.Children.Add(reference);
            Vector freshOffset=VisualTreeHelper.GetOffset(reference);
            // Reuse the same native visual and its already arranged offset. Do not redraw or relayout it.
            return $"originalOffset=({originalOffset.X:R},{originalOffset.Y:R}) freshOffset=({freshOffset.X:R},{freshOffset.Y:R}) offsetPreserved={originalOffset==freshOffset} referenceFlags={{"+originalFlags+"} pageFlags={"+pageFlags+"} repeatOriginal={"+repeat+"} freshLiveHost={"+VisualPdfRasterReplayAudit(fresh,expected)+"} freshFlags={"+VisualPdfVisualAudit(reference)+"}";
        }
        finally{fresh?.Children.Clear();}
    }
    private static string VisualPdfVisualAudit(Visual visual)
    {
        string X=string.Join(",",VisualTreeHelper.GetXSnappingGuidelines(visual)?.Take(16).Select(VisualPdfNumber)??Array.Empty<string>());
        string Y=string.Join(",",VisualTreeHelper.GetYSnappingGuidelines(visual)?.Take(16).Select(VisualPdfNumber)??Array.Empty<string>());
        return $"snapX=[{X}] snapY=[{Y}] opacity={VisualTreeHelper.GetOpacity(visual):R} edge={VisualTreeHelper.GetEdgeMode(visual)} cache={VisualTreeHelper.GetCacheMode(visual)?.GetType().Name??"null"} effect={VisualTreeHelper.GetEffect(visual)?.GetType().Name??"null"} clip={VisualTreeHelper.GetClip(visual)?.Bounds.ToString()??"null"} formatting={TextOptions.GetTextFormattingMode(visual)} rendering={TextOptions.GetTextRenderingMode(visual)} hinting={TextOptions.GetTextHintingMode(visual)} clearType={RenderOptions.GetClearTypeHint(visual)}";
    }
    private static string VisualPdfRasterReplayAudit(Visual visual,byte[] expected)
    {
        var bitmap=new RenderTargetBitmap(794,1123,96,96,PixelFormats.Pbgra32);
        byte[]? bgra=null,rgb=null;
        try
        {
            bitmap.Render(visual);bgra=new byte[794*1123*4];rgb=new byte[794*1123*3];bitmap.CopyPixels(bgra,794*4,0);
            for(int i=0,j=0;i<bgra.Length;i+=4,j+=3)
            {
                int alpha=bgra[i+3];double coverage=alpha/255.0;
                for(int channel=0;channel<3;channel++)
                {
                    double straight=alpha==0?0:bgra[i+2-channel]/coverage;
                    rgb[j+channel]=(byte)Math.Round(straight*coverage+255*(1-coverage),MidpointRounding.AwayFromZero);
                }
            }
            long sum=0;int peak=0,large=0;
            for(int i=0;i<rgb.Length;i+=3){int delta=0;for(int c=0;c<3;c++)delta=Math.Max(delta,Math.Abs(rgb[i+c]-expected[i+c]));sum+=delta;peak=Math.Max(peak,delta);if(delta>32)large++;}
            return $"entireExpectedRgbEqual={rgb.AsSpan().SequenceEqual(expected)} representative="+(rgb.AsSpan().SequenceEqual(expected)?"verified":"unverified")+$" sum={sum} peak={peak} large={large} "+VisualPdfBgraAudit(bgra);
        }
        finally{if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);bitmap.Clear();}
    }
    private static string VisualPdfDrawingAudit(Drawing? root)
    {
        var descriptions=new List<string>();
        void Inspect(Drawing? drawing,int depth)
        {
            if(drawing is null||depth>16||descriptions.Count>=16)return;
            if(drawing is GlyphRunDrawing glyph)
            {
                string brush=glyph.ForegroundBrush is SolidColorBrush solid?$"solid({solid.Color}) opacity={solid.Opacity:R}":glyph.ForegroundBrush?.GetType().Name??"null";
                descriptions.Add(VisualPdfGlyphAudit(glyph.GlyphRun)+" brush="+brush);return;
            }
            if(drawing is DrawingGroup group)
            {
                var matrix=group.Transform?.Value??Matrix.Identity;
                descriptions.Add($"group opacity={group.Opacity:R} matrix=({matrix.M11:R},{matrix.M12:R},{matrix.M21:R},{matrix.M22:R},{matrix.OffsetX:R},{matrix.OffsetY:R})"+
                    " guidelineX="+string.Join(",",group.GuidelineSet?.GuidelinesX.Take(8).Select(VisualPdfNumber)??Array.Empty<string>())+
                    " guidelineY="+string.Join(",",group.GuidelineSet?.GuidelinesY.Take(8).Select(VisualPdfNumber)??Array.Empty<string>()));
                foreach(var child in group.Children)Inspect(child,depth+1);
            }
        }
        Inspect(root,0);return string.Join(";",descriptions);
    }
    private static string VisualPdfNumber(double value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    private static string VisualPdfGlyphAudit(GlyphRun glyph)
    {
        var ink=glyph.ComputeInkBoundingBox();
        return "face="+Path.GetFileName(glyph.GlyphTypeface.FontUri.LocalPath)+" em="+VisualPdfNumber(glyph.FontRenderingEmSize)+" base=("+VisualPdfNumber(glyph.BaselineOrigin.X)+","+VisualPdfNumber(glyph.BaselineOrigin.Y)+") ink=("+VisualPdfNumber(ink.Left)+","+VisualPdfNumber(ink.Top)+","+VisualPdfNumber(ink.Right)+","+VisualPdfNumber(ink.Bottom)+") glyph="+string.Join(",",glyph.GlyphIndices.Take(32))+" advance="+string.Join(",",glyph.AdvanceWidths.Take(32).Select(VisualPdfNumber))+
            $" pixelsPerDip={glyph.PixelsPerDip:R} bidi={glyph.BidiLevel} sideways={glyph.IsSideways} style={glyph.GlyphTypeface.StyleSimulations} offsets="+string.Join(",",glyph.GlyphOffsets?.Take(32).Select(p=>$"({p.X:R},{p.Y:R})")??Array.Empty<string>());
    }
    private static bool VisualPdfWhiteBorder(byte[] rgb)
    {
        for(int row=0;row<1123;row++)for(int column=0;column<794;column++)
        {
            if(row>=2&&row<1121&&column>=2&&column<792)continue;int pixel=(row*794+column)*3;if(rgb[pixel]!=255||rgb[pixel+1]!=255||rgb[pixel+2]!=255)return false;
        }
        return true;
    }
}

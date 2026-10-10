using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            using var prepared=await PdfVisualRenderer.RenderPlainAsync([new("",text)],()=>true,CancellationToken.None);
            byte[] rgb=VisualPdfFirstRgb(prepared.Bytes),reference=VisualPdfTextBlockReference(text);
            try
            {
                Require(rgb.Length==794*1123*3&&VisualPdfReferenceMatches(rgb,reference),"Native TextFormatter/glyph-audited PDF pixels match independent TextBlock: "+Convert.ToHexString(Encoding.UTF8.GetBytes(text)));
                string grammar=Encoding.Latin1.GetString(prepared.Bytes);Require(grammar.Contains("/Subtype /Image")&&grammar.Contains("/DeviceRGB")&&!grammar.Contains("/ToUnicode")&&!grammar.Contains("/Type /Font"),"Display PDF contains raster pages and no searchable text/font objects");
            }
            finally{CryptographicOperations.ZeroMemory(rgb);CryptographicOperations.ZeroMemory(reference);}
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
                byte[] actual=VisualPdfFirstRgb(boundary.Bytes,page),reference=VisualPdfTextBlockReference(string.Join('\n',page==0?boundaryLines.Take(42):boundaryLines.Skip(42)));
                try{Require(VisualPdfReferenceMatches(actual,reference)&&VisualPdfWhiteBorder(actual),"Last full-page line and next-page ink match native reference without edge clipping: "+page);}
                finally{CryptographicOperations.ZeroMemory(actual);CryptographicOperations.ZeroMemory(reference);}
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
    private static byte[] VisualPdfTextBlockReference(string text)
    {
        string layout=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Replace("\t","    ",StringComparison.Ordinal);
        var page=new Grid{Width=794,Height=1123,Background=Brushes.White};
        var reference=new TextBlock{Text=layout,FontFamily=new FontFamily("Global User Interface"),FontSize=11*96.0/72,LineHeight=24,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.LeftToRight,Foreground=Brushes.Black,Width=210*96.0/25.4-2*40*96.0/72,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(40*96.0/72,40*96.0/72,0,0),Language=System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR")};
        TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);page.Children.Add(reference);page.Measure(new Size(794,1123));page.Arrange(new Rect(0,0,794,1123));page.UpdateLayout();
        var bitmap=new RenderTargetBitmap(794,1123,96,96,PixelFormats.Pbgra32);bitmap.Render(page);byte[] bgra=new byte[794*1123*4],rgb=new byte[794*1123*3];
        try
        {
            bitmap.CopyPixels(bgra,794*4,0);
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
            return rgb;
        }
        finally{CryptographicOperations.ZeroMemory(bgra);bitmap.Clear();page.Children.Clear();}
    }
    private static bool VisualPdfReferenceMatches(byte[] actual,byte[] expected)
    {
        long difference=0;int large=0,ink=0;for(int i=0;i<actual.Length;i+=3){if(expected[i]<250||expected[i+1]<250||expected[i+2]<250)ink++;int delta=Math.Max(Math.Abs(actual[i]-expected[i]),Math.Max(Math.Abs(actual[i+1]-expected[i+1]),Math.Abs(actual[i+2]-expected[i+2])));difference+=delta;if(delta>32)large++;}
        return large<=Math.Max(8,ink/20)&&difference<=Math.Max(64,ink*8);
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

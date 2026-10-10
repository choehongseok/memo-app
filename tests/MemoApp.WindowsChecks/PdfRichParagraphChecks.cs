using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;
using MemoApp.Windows;

internal static partial class Program
{
    // Native behavior RED first: renderer/host changes are not enabled by this fixture.
    private static async Task PdfRichParagraphRun()
    {
        byte[] reference=await PdfRichManualReference();
        try{Require(reference.Length==794*1123*3,"Independent styled native RGB reference has exact fixed page dimensions");}
        finally{CryptographicOperations.ZeroMemory(reference);}
        Console.WriteLine("N02 rich paragraph native control manualStyledReference=True; CASE existing-rich-route");
        string directory=Path.Combine(Path.GetTempPath(),"memo-wpf-pdf-rich-"+Guid.NewGuid().ToString("N")),root=Path.Combine(directory,"vault");Directory.CreateDirectory(directory);
        byte[] secret=EncryptedVault.GenerateRecoverySecret();byte[]? source=null,cipher=null,pdf=null;MainWindow? main=null;SaveCoordinator? active=null;Task<bool>? export=null;Exception? primary=null;
        try
        {
            main=new MainWindow(root);main.Show();Invoke(main,"StartSession",EncryptedVault.Create(root,secret,secret));active=Field<SaveCoordinator>(main,"session");Field<DispatcherTimer>(main,"timer").Stop();
            var note=active.Workspace.CreateNote();note.Title="";active.Workspace.ConvertMode(note,"rich",true);
            const string original="{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"합성 \"},{\"text\":\"굵은 한글 ABC\",\"bold\":true,\"underline\":true,\"strike\":true,\"fontFamily\":\"Malgun Gothic\",\"foreground\":\"#CC0000\",\"background\":\"#80FFFF00\"},{\"text\":\" é 😀\"}]}]}";
            active.Workspace.SetRichDocument(note,new StyledDocument(1,original));
            Require(RichDocumentCodec.Inspect(note.Document!).Supported&&note.Document!.SourceJson==original,"RED fixture is an actual supported canonical v1 paragraph with immutable original run styles");
            Invoke(main,"RefreshNotes",note);Task recent=Field<Task>(main,"recentTask");await Idle();await recent;await Idle();
            var list=Control<ListBox>(main,"NotesList");list.UnselectAll();list.SelectedItem=note;
            Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItems[0],note),"Native RED route has exactly the original selected live note");
            recent=Field<Task>(main,"recentTask");await Idle();await recent;await Idle();
            Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItems[0],note),"Exact selection recent task settles before the explicit source checkpoint");
            Require(await active.SaveAsync(),"Explicit rich paragraph fixture checkpoint saves before the export oracle");
            recent=Field<Task>(main,"recentTask");await Idle();await recent;await Idle();
            Require(list.SelectedItems.Count==1&&ReferenceEquals(list.SelectedItems[0],note)&&note.Document!.SourceJson==original,"Real recent work settles before frozen rich export source authority");
            source=PdfAuthorityStableSource(active.Workspace);cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));long version=note.EditVersion,epoch=active.AttachmentPreviewEpoch;int confirmations=0,pickers=0;
            string destination=Path.Combine(directory,"rich-paragraph.pdf");var method=typeof(MainWindow).GetMethod("ExportSelectedVisualPdfAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            export=(Task<bool>)method.Invoke(main,[new Func<bool>(()=>{confirmations++;return true;}),new Func<string?>(()=>{pickers++;return destination;}),null])!;
            bool accepted=await export;
            Console.WriteLine($"N02 rich paragraph existing route accepted={accepted} confirmations={confirmations} pickers={pickers} fileCreated={File.Exists(destination)} sourceExact={note.Document!.SourceJson==original} versionExact={note.EditVersion==version} epochExact={active.AttachmentPreviewEpoch==epoch}");
            Require(confirmations==1&&pickers==1,"Real display PDF route reached explicit consent and external destination before missing-rich-route RED");
            byte[] afterSource=PdfAuthorityStableSource(active.Workspace);bool stableSource;try{stableSource=source.SequenceEqual(afterSource);}finally{CryptographicOperations.ZeroMemory(afterSource);}
            byte[] afterCipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));bool stableCipher;try{stableCipher=cipher.SequenceEqual(afterCipher);}finally{CryptographicOperations.ZeroMemory(afterCipher);}
            Require(note.Document!.SourceJson==original&&note.EditVersion==version&&active.AttachmentPreviewEpoch==epoch&&stableSource&&stableCipher,"Rich PDF attempt preserves original source/style/version/epoch/history and exact ciphertext");
            Require(accepted,"N02 native behavioral RED: existing display PDF route must export the supported rich v1 styled paragraph after positive native reference controls");
            Require(File.Exists(destination),"Accepted rich paragraph route creates its complete external PDF");pdf=File.ReadAllBytes(destination);var grammar=System.Text.Encoding.Latin1.GetString(pdf);
            Require(grammar.StartsWith("%PDF-",StringComparison.Ordinal)&&grammar.Contains("/Subtype /Image",StringComparison.Ordinal)&&!grammar.Contains("/Type /Font",StringComparison.Ordinal)&&!grammar.Contains("/ToUnicode",StringComparison.Ordinal),"Rich display PDF remains a raster document without embedded fonts or searchable text");
        }
        catch(Exception error){primary=error;throw;}
        finally
        {
            var failures=new List<Exception>();bool cleaned=active is null;
            try
            {
                if(export is not null)try{await export;}catch(Exception error){failures.Add(error);}
                if(active is not null)
                {
                    try{await active.LockAsync();await active.WhenAttachmentReadsIdle;}catch(Exception error){failures.Add(error);}
                    if(active.KeysReleased&&!active.IsBusy)try{Invoke(main!,"ReleaseSettledSession");active=null;cleaned=true;}catch(Exception error){failures.Add(error);}
                }
                if(main is not null)try{SetField(main,"confirmedExit",true);main.Close();}catch(Exception error){failures.Add(error);}
            }
            finally
            {
                if(source is not null)CryptographicOperations.ZeroMemory(source);if(cipher is not null)CryptographicOperations.ZeroMemory(cipher);if(pdf is not null)CryptographicOperations.ZeroMemory(pdf);CryptographicOperations.ZeroMemory(secret);
                if(cleaned&&Directory.Exists(directory))try{Directory.Delete(directory,true);}catch(Exception error){failures.Add(error);}
            }
            if(failures.Count>0){if(primary is not null)failures.Insert(0,primary);throw new AggregateException("Synthetic rich PDF fixture cleanup failed",failures);}
        }
    }
    private static async Task<byte[]> PdfRichManualReference()
    {
        var page=new Grid{Width=794,Height=1123,Background=Brushes.White};
        var text=new TextBlock{FontFamily=new FontFamily("Segoe UI"),FontSize=14,Foreground=Brushes.Black,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.LeftToRight,Language=XmlLanguage.GetLanguage("ko-KR"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Width=600,Margin=new Thickness(54)};
        text.Inlines.Add(new Run("합성 "));
        var decorations=new TextDecorationCollection{TextDecorations.Underline[0],TextDecorations.Strikethrough[0]};
        text.Inlines.Add(new Run("굵은 한글 ABC"){FontFamily=new FontFamily("Malgun Gothic"),FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(204,0,0)),Background=new SolidColorBrush(Color.FromArgb(128,255,255,0)),TextDecorations=decorations});
        text.Inlines.Add(new Run(" é 😀"));page.Children.Add(text);TextOptions.SetTextFormattingMode(page,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(page,TextRenderingMode.Grayscale);
        var window=new Window{Content=page,Width=820,Height=700};byte[]? bgra=null,rgb=null;bool transferred=false;Exception? primary=null;
        try
        {
            window.Show();await Idle();page.Measure(new Size(794,1123));page.Arrange(new Rect(0,0,794,1123));page.UpdateLayout();
            Require(new TextRange(text.ContentStart,text.ContentEnd).Text.Contains("굵은 한글 ABC é 😀",StringComparison.Ordinal),"Manually constructed native run graph contains the exact styled Unicode reference body");
            var bitmap=new RenderTargetBitmap(794,1123,96,96,PixelFormats.Pbgra32);bitmap.Render(page);bgra=new byte[794*1123*4];rgb=new byte[794*1123*3];bitmap.CopyPixels(bgra,794*4,0);
            int red=0,yellow=0,ink=0;
            for(int i=0,j=0;i<bgra.Length;i+=4,j+=3)
            {
                int alpha=bgra[i+3];Require(bgra[i]<=alpha&&bgra[i+1]<=alpha&&bgra[i+2]<=alpha,"Independent styled native reference has valid premultiplied channels");double coverage=alpha/255.0;
                for(int channel=0;channel<3;channel++){double straight=alpha==0?0:bgra[i+2-channel]/coverage;rgb[j+channel]=(byte)Math.Round(straight*coverage+255*(1-coverage),MidpointRounding.AwayFromZero);}
                if(rgb[j]<240||rgb[j+1]<240||rgb[j+2]<240)ink++;if(rgb[j]>rgb[j+1]+40&&rgb[j]>rgb[j+2]+40)red++;if(rgb[j]>220&&rgb[j+1]>220&&rgb[j+2]<220)yellow++;
            }
            Require(ink>100&&red>20&&yellow>20,"Actual independent native reference contains glyph ink, requested red foreground and alpha yellow highlight before rich route RED");
            Console.WriteLine($"N02 rich paragraph manual native reference populated=True inkPixels={ink} redPixels={red} highlightPixels={yellow}");transferred=true;return rgb;
        }
        catch(Exception error){primary=error;throw;}
        finally
        {
            var failures=new List<Exception>();if(bgra is not null)CryptographicOperations.ZeroMemory(bgra);if(!transferred&&rgb is not null)CryptographicOperations.ZeroMemory(rgb);
            foreach(Action cleanup in new Action[]{()=>text.Inlines.Clear(),()=>page.Children.Clear(),()=>window.Content=null,()=>window.Close()})try{cleanup();}catch(Exception error){failures.Add(error);}
            if(failures.Count>0){if(rgb is not null)CryptographicOperations.ZeroMemory(rgb);if(primary is not null)failures.Insert(0,primary);throw new AggregateException("Synthetic styled native reference cleanup failed",failures);}
        }
    }
}

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MemoApp.Core.Editing;
namespace MemoApp.Core.Transfer;
// Plain verified text, embedded fixed font, no scripts, URLs, source attachments or PDF input parser.
public static class PdfTextTransfer
{
    public const int MaxPages=256;
    private const int MaxBytes=16*1024*1024,LinesPerPage=48,MaxCids=16384;
    public static PreparedTextExport Capture(IEnumerable<NoteDraft> selection,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(selection);var notes=selection.Take(101).ToArray();if(notes.Length is <1 or >100||notes.Select(n=>n.Id).Distinct().Count()!=notes.Length)throw new ArgumentException("PDF selection limit");
        var font=PdfExportFont.Shared.Value;var cids=new Dictionary<char,ushort>();var pages=new List<byte[]>();int total=0;
        try
        {
            foreach(var note in notes)
            {
                cancellationToken.ThrowIfCancellationRequested();using var validated=TextTransfer.Capture(note);total=checked(total+validated.Bytes.Length);if(total>MaxBytes)throw new InvalidDataException("PDF source limit");
                string text=(note.Title.Length==0?"":note.Title+"\n\n")+note.Text; // Raw Markdown is a declared plain-text projection.
                foreach(char ch in text)
                {
                    cancellationToken.ThrowIfCancellationRequested();if(ch is '\r' or '\n' or '\t')continue;
                    var category=char.GetUnicodeCategory(ch);bool script=ch is >= ' ' and <= '\u02ff' or >= '\u0370' and <= '\u052f' or >= '\u2000' and <= '\u2bff' or >= '\u3000' and <= '\u30ff' or >= '\u3130' and <= '\u318f' or >= '\u3400' and <= '\u9fff' or >= '\uac00' and <= '\ud7a3' or >= '\uff01' and <= '\uff5e';
                    if(!script||category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Surrogate||char.IsSurrogate(ch))throw new InvalidDataException("PDF source requires unsupported shaping or Unicode coverage");
                    _=font.Glyph(ch);if(!cids.ContainsKey(ch)){if(cids.Count>=MaxCids)throw new InvalidDataException("PDF character map limit");cids.Add(ch,checked((ushort)(cids.Count+1)));}
                }
                if(!cids.ContainsKey(' ')){if(cids.Count>=MaxCids)throw new InvalidDataException("PDF character map limit");cids.Add(' ',checked((ushort)(cids.Count+1)));}
                Layout(text,font,cids,pages,cancellationToken);
            }
            using var output=new BoundedPdfBuffer(MaxBytes);var positions=new List<long>{0};
            void Ascii(string s)=>output.Write(Encoding.ASCII.GetBytes(s));
            void Object(int id,string dictionary){if(id!=positions.Count)throw new InvalidOperationException("PDF object ordering");positions.Add(output.Length);Ascii($"{id} 0 obj\n{dictionary}\nendobj\n");}
            void Stream(int id,ReadOnlySpan<byte> bytes,string extra="")
            {if(id!=positions.Count)throw new InvalidOperationException("PDF stream ordering");positions.Add(output.Length);Ascii($"{id} 0 obj\n<< /Length {bytes.Length} {extra} >>\nstream\n");output.Write(bytes);Ascii("\nendstream\nendobj\n");}
            Ascii("%PDF-1.7\n");output.Write([0x25,0xe2,0xe3,0xcf,0xd3,0x0a]);
            Object(1,"<< /Type /Catalog /Pages 2 0 R >>");Object(2,$"<< /Type /Pages /Count {pages.Count} /Kids ["+string.Join(" ",Enumerable.Range(0,pages.Count).Select(i=>$"{9+i*2} 0 R"))+"] >>");
            Object(3,"<< /Type /Font /Subtype /Type0 /BaseFont /D2Coding /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 8 0 R >>");
            var widths=new StringBuilder();foreach(var pair in cids.OrderBy(p=>p.Value))widths.Append(font.Width(pair.Key).ToString(CultureInfo.InvariantCulture)).Append(' ');
            Object(4,"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /D2Coding /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 5 0 R /CIDToGIDMap 7 0 R /DW 500 /W [1 ["+widths+"]] >>");
            Object(5,"<< /Type /FontDescriptor /FontName /D2Coding /Flags 33 /FontBBox [-1489 -287 1028 913] /ItalicAngle 0 /Ascent 850 /Descent -230 /CapHeight 700 /StemV 80 /FontFile2 6 0 R >>");
            // Font bytes are public and unmodified; embedding keeps the complete font and its original name.
            Stream(6,font.Bytes,$"/Length1 {PdfExportFont.Length}");byte[] glyphMap=new byte[(cids.Count+1)*2];try{foreach(var pair in cids)BinaryPrimitives.WriteUInt16BigEndian(glyphMap.AsSpan(pair.Value*2,2),font.Glyph(pair.Key));Stream(7,glyphMap);}finally{CryptographicOperations.ZeroMemory(glyphMap);}
            using(var map=new BoundedPdfBuffer(1024*1024))
            {
                void MapAscii(string s)=>map.Write(Encoding.ASCII.GetBytes(s));MapAscii("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /MemoAppUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
                foreach(var block in cids.OrderBy(p=>p.Value).Chunk(100)){MapAscii(block.Length+" beginbfchar\n");foreach(var pair in block){MapAscii("<");Hex(map,pair.Value);MapAscii("> <");Hex(map,pair.Key);MapAscii(">\n");}MapAscii("endbfchar\n");}MapAscii("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");Stream(8,map.Written);
            }
            for(int i=0;i<pages.Count;i++){cancellationToken.ThrowIfCancellationRequested();Object(9+i*2,$"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {10+i*2} 0 R >>");Stream(10+i*2,pages[i]);}
            long xref=output.Length;Ascii($"xref\n0 {positions.Count}\n0000000000 65535 f \n");foreach(long position in positions.Skip(1))Ascii(position.ToString("D10",CultureInfo.InvariantCulture)+" 00000 n \n");Ascii($"trailer\n<< /Size {positions.Count} /Root 1 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");cancellationToken.ThrowIfCancellationRequested();return new(output.Copy());
        }
        finally{foreach(var page in pages)CryptographicOperations.ZeroMemory(page);pages.Clear();cids.Clear();}
    }
    private static void Layout(string text,PdfExportFont font,Dictionary<char,ushort> cids,List<byte[]> pages,CancellationToken token)
    {
        using var page=new BoundedPdfBuffer(32768);var line=new List<char>(100);int lines=0,width=0;
        void Page(){if(pages.Count>=MaxPages)throw new InvalidDataException("PDF page limit");pages.Add(page.Copy());page.Reset();lines=0;}
        void Line()
        {
            if(lines==LinesPerPage)Page();page.Write(Encoding.ASCII.GetBytes($"BT /F1 11 Tf 1 0 0 1 40 {802-lines*16} Tm <"));foreach(char ch in line)Hex(page,cids[ch]);page.Write(" > Tj ET\n"u8);line.Clear();width=0;lines++;
        }
        void Character(char ch){int advance=font.Width(ch)*11;if(width+advance>515000||line.Count>=100)Line();line.Add(ch);width+=advance;}
        for(int i=0;i<text.Length;i++){token.ThrowIfCancellationRequested();char ch=text[i];if(ch=='\r'){if(i+1<text.Length&&text[i+1]=='\n')i++;Line();}else if(ch=='\n')Line();else if(ch=='\t'){for(int t=0;t<4;t++)Character(' ');}else Character(ch);}
        Line();Page();
    }
    private static void Hex(BoundedPdfBuffer output,ushort value)
    {ReadOnlySpan<byte> digits="0123456789ABCDEF"u8;Span<byte> bytes=stackalloc byte[4];for(int i=0;i<4;i++)bytes[i]=digits[(value>>((3-i)*4))&15];output.Write(bytes);}
    private sealed class BoundedPdfBuffer(int limit):IDisposable
    {
        private byte[] bytes=new byte[Math.Min(limit,256)];private int count;
        internal long Length=>count;internal ReadOnlySpan<byte> Written=>bytes.AsSpan(0,count);
        internal void Write(ReadOnlySpan<byte> data){int needed=checked(count+data.Length);if(needed>limit)throw new InvalidDataException("PDF construction limit");if(needed>bytes.Length){var grown=new byte[Math.Min(limit,Math.Max(needed,checked(bytes.Length*2)))];bytes.AsSpan(0,count).CopyTo(grown);CryptographicOperations.ZeroMemory(bytes);bytes=grown;}data.CopyTo(bytes.AsSpan(count));count=needed;}
        internal byte[] Copy()=>Written.ToArray();internal void Reset(){CryptographicOperations.ZeroMemory(bytes);count=0;}public void Dispose(){Reset();bytes=[];}
    }
}

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
namespace MemoApp.Core.Transfer;
// Only this immutable, hash-checked public font is parsed. No user or system font input.
internal sealed class PdfExportFont
{
    internal static readonly Lazy<PdfExportFont> Shared=new(()=>new());
    internal const int Length=4119796;
    internal readonly byte[] Bytes;
    private readonly Dictionary<string,(int Offset,int Length)> tables=[];
    private readonly ushort[] glyphs=new ushort[65536];
    private readonly int metrics,glyphCount;
    private PdfExportFont()
    {
        byte[] packed=new byte[2051259];
        for(int i=0;i<32;i++)
        {
            using var part=typeof(PdfExportFont).Assembly.GetManifestResourceStream("MemoApp.PdfFont.D2Coding1.4.0-"+i.ToString("D2",System.Globalization.CultureInfo.InvariantCulture))??throw new InvalidDataException("PDF font part missing");
            int offset=i*65536,count=Math.Min(65536,packed.Length-offset);part.ReadExactly(packed.AsSpan(offset,count));if(part.ReadByte()!=-1)throw new InvalidDataException("PDF font part length");
        }
        if(Convert.ToHexStringLower(SHA256.HashData(packed))!="2f332f3a70c551b36761e68ac76787b6a3957b9888a87d97ab16a50957503bf4")throw new InvalidDataException("PDF compressed font hash");
        using var source=new MemoryStream(packed,false);using var compressed=new ZLibStream(source,CompressionMode.Decompress);Bytes=new byte[Length];compressed.ReadExactly(Bytes);if(compressed.ReadByte()!=-1||Convert.ToHexStringLower(SHA256.HashData(Bytes))!="d5f2b6bf5b1826cfbf3734c70582f0cd42ea2e1d6c2636ae681b612bc9c5682f")throw new InvalidDataException("PDF font hash/length");
        if(U32(0)!=0x00010000||U16(4)>64)throw new InvalidDataException("Fixed PDF font header");
        for(int i=0;i<U16(4);i++){int at=12+i*16;string tag=System.Text.Encoding.ASCII.GetString(Bytes,at,4);int offset=checked((int)U32(at+8)),length=checked((int)U32(at+12));if(offset<0||length<0||offset>Bytes.Length-length||!tables.TryAdd(tag,(offset,length)))throw new InvalidDataException("Fixed PDF font table");}
        if(U16(Table("head",54)+18)!=1000||U16(Table("OS/2",10)+8)!=8)throw new InvalidDataException("Fixed font units/embedding");glyphCount=U16(Table("maxp",6)+4);metrics=U16(Table("hhea",36)+34);if(glyphCount!=26190||metrics!=26190)throw new InvalidDataException("Fixed font metrics");_=Table("hmtx",metrics*4);
        int cmap=Table("cmap",4),records=U16(cmap+2);if(records>16)throw new InvalidDataException("Fixed font cmap limit");int format=0;
        for(int i=0;i<records;i++){int at=cmap+4+i*8;if(U16(at)==3&&U16(at+2)==1){format=checked(cmap+(int)U32(at+4));break;}}
        if(format==0||U16(format)!=4)throw new InvalidDataException("Fixed format4 cmap required");int length4=U16(format+2),segments=U16(format+6)/2;if(segments is <1 or >8192||format<cmap||format+length4>cmap+tables["cmap"].Length||length4<16+segments*8)throw new InvalidDataException("Fixed format4 cmap bounds");
        int ends=format+14,starts=ends+segments*2+2,deltas=starts+segments*2,ranges=deltas+segments*2,previous=-1;
        for(int i=0;i<segments;i++)
        {
            int end=U16(ends+i*2),start=U16(starts+i*2),delta=U16(deltas+i*2),range=U16(ranges+i*2);if(start>end||start<=previous)throw new InvalidDataException("Fixed cmap segment order");previous=end;
            for(int ch=start;ch<=end;ch++){int glyph;if(range==0)glyph=(ch+delta)&65535;else{int at=ranges+i*2+range+(ch-start)*2;if(at<format||at>format+length4-2)throw new InvalidDataException("Fixed cmap glyph bounds");glyph=U16(at);if(glyph!=0)glyph=(glyph+delta)&65535;}if(glyph>=glyphCount)throw new InvalidDataException("Fixed cmap glyph identity");glyphs[ch]=(ushort)glyph;}
        }
    }
    private int Table(string tag,int minimum)=>tables.TryGetValue(tag,out var t)&&t.Length>=minimum?t.Offset:throw new InvalidDataException("Fixed font table missing");
    private ushort U16(int at)=>BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(at,2));
    private uint U32(int at)=>BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(at,4));
    internal ushort Glyph(char ch)=>glyphs[ch]!=0?glyphs[ch]:throw new InvalidDataException("PDF font does not contain a source character");
    internal int Width(char ch)=>U16(tables["hmtx"].Offset+Glyph(ch)*4);
}

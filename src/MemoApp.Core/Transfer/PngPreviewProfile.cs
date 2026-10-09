using System.Buffers.Binary;
namespace MemoApp.Core.Transfer;

// Structure only. A successful result grants no valid-pixel, decode, session or publication authority.
internal sealed record PngPreviewHeader(int Width,int Height,int Channels,int SourcePixels,int BgraBytes,
    int PreviewWidth,int PreviewHeight,int PreviewPixels,int ChunkCount,int StructuralBytes,int DataBytes);
internal static class PngPreviewProfile
{
    private static readonly uint[] CrcTable=MakeCrcTable();
    internal static PngPreviewHeader Inspect(ReadOnlySpan<byte> source)
    {
        if(source.Length is <8 or >4194304||!source[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw Refused();
        int position=8,chunks=0,width=0,height=0,channels=0,pixels=0,dataBytes=0,idat=0,structure=8;
        bool srgb=false,gamma=false,density=false;
        while(position<source.Length)
        {
            int remaining=source.Length-position;if(remaining<12)throw Refused();
            uint raw=BinaryPrimitives.ReadUInt32BigEndian(source[position..]);
            if(raw>int.MaxValue||raw>(uint)(remaining-12))throw Refused();
            int length=(int)raw;var type=source.Slice(position+4,4);int end=checked(position+12+length);
            if(++chunks>256)throw Refused();
            if(chunks==1)
            {
                if(!type.SequenceEqual("IHDR"u8)||length!=13)throw Refused();
                var header=source.Slice(position+8,13);uint w=BinaryPrimitives.ReadUInt32BigEndian(header),h=BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                if(w is <1 or >4096||h is <1 or >4096||header[8]!=8||header[9] is not (2 or 6)||header[10]!=0||header[11]!=0||header[12]!=0)throw Refused();
                long total=checked((long)w*h);if(total>4194304)throw Refused();width=(int)w;height=(int)h;pixels=(int)total;channels=header[9]==6?4:3;
                structure=checked(structure+length);
            }
            else if(type.SequenceEqual("sRGB"u8))
            {if(srgb||idat!=0||length!=1||source[position+8]>3)throw Refused();srgb=true;structure=checked(structure+length);}
            else if(type.SequenceEqual("gAMA"u8))
            {if(gamma||idat!=0||length!=4||BinaryPrimitives.ReadUInt32BigEndian(source.Slice(position+8,length)) is 0 or >int.MaxValue)throw Refused();gamma=true;structure=checked(structure+length);}
            else if(type.SequenceEqual("pHYs"u8))
            {if(density||idat!=0||length!=9||source[position+16]>1||BinaryPrimitives.ReadUInt32BigEndian(source.Slice(position+8,4))>int.MaxValue||BinaryPrimitives.ReadUInt32BigEndian(source.Slice(position+12,4))>int.MaxValue)throw Refused();density=true;structure=checked(structure+length);}
            else if(type.SequenceEqual("IDAT"u8)){idat++;dataBytes=checked(dataBytes+length);}
            else if(!type.SequenceEqual("IEND"u8)||length!=0||idat==0||dataBytes==0||end!=source.Length)throw Refused();
            structure=checked(structure+12);if(structure>65536)throw Refused(); // Fixed scalar ancillary payloads are included; no metadata is decoded or rendered.
            uint actual=Crc(source.Slice(position+4,checked(length+4))),stored=BinaryPrimitives.ReadUInt32BigEndian(source.Slice(position+8+length,4));
            if(actual!=stored)throw Refused();
            if(type.SequenceEqual("IEND"u8))
            {
                double scale=Math.Min(1.0,1024.0/Math.Max(width,height));
                int previewWidth=Math.Max(1,(int)Math.Floor(width*scale)),previewHeight=Math.Max(1,(int)Math.Floor(height*scale));
                int previewPixels=checked(previewWidth*previewHeight);if(previewWidth>1024||previewHeight>1024||previewPixels>1048576)throw Refused();
                return new(width,height,channels,pixels,checked(pixels*4),previewWidth,previewHeight,previewPixels,chunks,structure,dataBytes);
            }
            position=end;
        }
        throw Refused();
    }
    private static InvalidDataException Refused()=>new("Unsupported PNG preview structure profile");
    private static uint Crc(ReadOnlySpan<byte> data)
    {uint value=uint.MaxValue;foreach(byte b in data)value=CrcTable[(value^b)&255]^(value>>8);return value^uint.MaxValue;}
    private static uint[] MakeCrcTable()
    {
        var table=new uint[256];for(uint i=0;i<256;i++){uint value=i;for(int bit=0;bit<8;bit++)value=(value&1)!=0?(value>>1)^0xedb88320U:value>>1;table[i]=value;}return table;
    }
}
